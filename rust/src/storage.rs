use crate::error::{AppError, AppResult};
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs,
    io::Write,
    path::{Path, PathBuf},
};
use windows::Win32::{
    Foundation::LocalFree,
    Security::Cryptography::{
        CRYPT_INTEGER_BLOB, CRYPTPROTECT_UI_FORBIDDEN, CryptProtectData, CryptUnprotectData,
    },
};
use zeroize::Zeroizing;

pub const ENDPOINT: &str = "wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async";
#[derive(Clone, Serialize, Deserialize)]
#[serde(default, rename_all = "PascalCase")]
pub struct Preferences {
    pub enabled: bool,
    pub start_with_windows: bool,
    pub show_partial: bool,
    pub microphone_device_id: Option<String>,
    pub enable_audio_warmup: bool,
    pub enable_device_warmup: bool,
    pub recording_trigger_mode: u32,
    #[serde(flatten)]
    pub extra: BTreeMap<String, Value>,
}
impl Default for Preferences {
    fn default() -> Self {
        Self {
            enabled: true,
            start_with_windows: true,
            show_partial: true,
            microphone_device_id: None,
            enable_audio_warmup: true,
            enable_device_warmup: false,
            recording_trigger_mode: 0,
            extra: BTreeMap::new(),
        }
    }
}
#[derive(Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct Provider {
    #[serde(default)]
    pub app_id: String,
    #[serde(default)]
    pub access_token: String,
    #[serde(default)]
    pub api_key: String,
    #[serde(default = "default_resource")]
    pub resource_id: String,
    #[serde(default = "default_endpoint")]
    pub endpoint: String,
    #[serde(flatten)]
    pub extra: BTreeMap<String, Value>,
}
fn default_resource() -> String {
    "volc.seedasr.sauc.duration".into()
}
fn default_endpoint() -> String {
    ENDPOINT.into()
}
impl Default for Provider {
    fn default() -> Self {
        Self {
            app_id: String::new(),
            access_token: String::new(),
            api_key: String::new(),
            resource_id: "volc.seedasr.sauc.duration".into(),
            endpoint: ENDPOINT.into(),
            extra: BTreeMap::new(),
        }
    }
}
impl Provider {
    pub fn has_credentials(&self) -> bool {
        !self.api_key.trim().is_empty()
            || (!self.app_id.trim().is_empty() && !self.access_token.trim().is_empty())
    }
    pub fn validate(&self) -> AppResult<()> {
        if !self.has_credentials()
            || self.resource_id.trim().is_empty()
            || [
                &self.api_key,
                &self.app_id,
                &self.access_token,
                &self.resource_id,
            ]
            .iter()
            .any(|s| s.chars().any(char::is_control))
        {
            return Err(AppError::Credentials);
        }
        Ok(())
    }
}
#[derive(Clone)]
pub struct PortablePaths {
    pub data: PathBuf,
    pub recordings: PathBuf,
}
impl PortablePaths {
    pub fn from_exe() -> AppResult<Self> {
        let root = std::env::current_exe()?
            .parent()
            .ok_or(AppError::Storage)?
            .to_owned();
        let p = Self {
            data: root.join("data"),
            recordings: root.join("recordings"),
        };
        fs::create_dir_all(p.data.join("credentials"))?;
        fs::create_dir_all(&p.recordings)?;
        if let Some(local) = std::env::var_os("LOCALAPPDATA") {
            let legacy = PathBuf::from(local).join("VoiceTyper");
            copy_missing(&legacy.join("settings.json"), &p.data.join("settings.json"))?;
            if let Ok(files) = fs::read_dir(legacy.join("credentials")) {
                for f in files.flatten() {
                    if f.path().is_file() {
                        copy_missing(&f.path(), &p.data.join("credentials").join(f.file_name()))?;
                    }
                }
            }
        }
        Ok(p)
    }
    fn credential(&self) -> PathBuf {
        self.data.join("credentials").join(format!(
            "{:X}.bin",
            Sha256::digest(b"volcengine-options-v1")
        ))
    }
}
fn copy_missing(source: &Path, target: &Path) -> AppResult<()> {
    if source.is_file() && !target.exists() {
        let temp = target.with_extension("migrate.tmp");
        fs::copy(source, &temp)?;
        fs::rename(temp, target)?;
    }
    Ok(())
}
pub fn atomic_write(path: &Path, bytes: &[u8]) -> AppResult<()> {
    use std::os::windows::ffi::OsStrExt;
    use windows::{
        Win32::Storage::FileSystem::{
            MOVEFILE_REPLACE_EXISTING, MOVEFILE_WRITE_THROUGH, MoveFileExW,
        },
        core::PCWSTR,
    };
    let temp = path.with_extension("write.tmp");
    let mut f = fs::File::create(&temp)?;
    f.write_all(bytes)?;
    f.sync_all()?;
    drop(f);
    let a: Vec<u16> = temp.as_os_str().encode_wide().chain(Some(0)).collect();
    let b: Vec<u16> = path.as_os_str().encode_wide().chain(Some(0)).collect();
    unsafe {
        MoveFileExW(
            PCWSTR(a.as_ptr()),
            PCWSTR(b.as_ptr()),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH,
        )?;
    }
    Ok(())
}
pub struct Configuration {
    pub preferences: Preferences,
    pub provider: Provider,
    pub credentials_need_reentry: bool,
}
impl Configuration {
    pub fn load(paths: &PortablePaths) -> AppResult<Self> {
        let mut preferences: Preferences = match fs::read(paths.data.join("settings.json")) {
            Ok(v) => serde_json::from_slice(&v).unwrap_or_default(),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Preferences::default(),
            Err(e) => return Err(e.into()),
        };
        preferences.recording_trigger_mode = u32::from(preferences.recording_trigger_mode == 1);
        let mut need = false;
        let mut provider = match fs::read(paths.credential()) {
            Ok(v) => match unprotect(&v)
                .and_then(|p| serde_json::from_slice(&p).map_err(|_| AppError::Credentials))
            {
                Ok(p) => p,
                Err(_) => {
                    need = true;
                    Provider::default()
                }
            },
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Provider::default(),
            Err(e) => return Err(e.into()),
        };
        provider.endpoint = ENDPOINT.into();
        Ok(Self {
            preferences,
            provider,
            credentials_need_reentry: need,
        })
    }
    pub fn save(&self, paths: &PortablePaths, credentials_changed: bool) -> AppResult<()> {
        if credentials_changed {
            self.provider.validate()?;
            let plain =
                Zeroizing::new(serde_json::to_vec(&self.provider).map_err(|_| AppError::Storage)?);
            atomic_write(&paths.credential(), &protect(&plain)?)?;
        }
        atomic_write(
            &paths.data.join("settings.json"),
            &serde_json::to_vec_pretty(&self.preferences).map_err(|_| AppError::Storage)?,
        )
    }
}
fn crypt(input: &[u8], encrypt: bool) -> AppResult<Zeroizing<Vec<u8>>> {
    let entropy = Sha256::digest(b"volcengine-options-v1");
    let data = CRYPT_INTEGER_BLOB {
        cbData: input.len() as u32,
        pbData: input.as_ptr() as *mut u8,
    };
    let salt = CRYPT_INTEGER_BLOB {
        cbData: entropy.len() as u32,
        pbData: entropy.as_ptr() as *mut u8,
    };
    let mut out = CRYPT_INTEGER_BLOB::default();
    unsafe {
        let result = if encrypt {
            CryptProtectData(
                &data,
                None,
                Some(&salt),
                None,
                None,
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out,
            )
        } else {
            CryptUnprotectData(
                &data,
                None,
                Some(&salt),
                None,
                None,
                CRYPTPROTECT_UI_FORBIDDEN,
                &mut out,
            )
        };
        result.map_err(|_| AppError::Credentials)?;
        let value =
            Zeroizing::new(std::slice::from_raw_parts(out.pbData, out.cbData as usize).to_vec());
        if !encrypt {
            std::ptr::write_bytes(out.pbData, 0, out.cbData as usize);
        }
        let _ = LocalFree(Some(windows::Win32::Foundation::HLOCAL(out.pbData.cast())));
        Ok(value)
    }
}
impl Drop for Provider {
    fn drop(&mut self) {
        use zeroize::Zeroize;
        self.api_key.zeroize();
        self.access_token.zeroize();
        self.app_id.zeroize();
    }
}
fn protect(v: &[u8]) -> AppResult<Zeroizing<Vec<u8>>> {
    crypt(v, true)
}
fn unprotect(v: &[u8]) -> AppResult<Zeroizing<Vec<u8>>> {
    crypt(v, false)
}
