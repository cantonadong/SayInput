use crate::error::AppResult;
use windows::{
    Win32::{
        Foundation::{CloseHandle, HANDLE},
        System::{
            Com::{COINIT_MULTITHREADED, CoInitializeEx, CoUninitialize},
            Threading::{CreateEventW, SetEvent, WaitForSingleObject},
        },
    },
    core::{HSTRING, PCWSTR},
};
pub fn wide(s: &str) -> Vec<u16> {
    s.encode_utf16().chain(Some(0)).collect()
}
pub struct Com;
impl Com {
    pub fn new() -> AppResult<Self> {
        unsafe {
            CoInitializeEx(None, COINIT_MULTITHREADED).ok()?;
        }
        Ok(Self)
    }
}
impl Drop for Com {
    fn drop(&mut self) {
        unsafe {
            CoUninitialize();
        }
    }
}
pub struct Event(pub HANDLE);
unsafe impl Send for Event {}
unsafe impl Sync for Event {}
impl Event {
    pub fn new(manual: bool) -> AppResult<Self> {
        Ok(Self(unsafe {
            CreateEventW(None, manual, false, PCWSTR::null())?
        }))
    }
    pub fn set(&self) {
        unsafe {
            let _ = SetEvent(self.0);
        }
    }
    pub fn signaled(&self) -> bool {
        unsafe { WaitForSingleObject(self.0, 0).0 == 0 }
    }
}
impl Drop for Event {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseHandle(self.0);
        }
    }
}
pub fn startup(enabled: bool) -> AppResult<()> {
    use windows::Win32::System::Registry::*;
    unsafe {
        let mut key = HKEY::default();
        RegCreateKeyExW(
            HKEY_CURRENT_USER,
            &HSTRING::from("Software\\Microsoft\\Windows\\CurrentVersion\\Run"),
            Some(0),
            None,
            REG_OPTION_NON_VOLATILE,
            KEY_SET_VALUE,
            None,
            &mut key,
            None,
        )
        .ok()?;
        let result = if enabled {
            let path = std::env::current_exe()?;
            let value = wide(&format!("\"{}\" --background", path.display()));
            RegSetValueExW(
                key,
                &HSTRING::from("SayInput"),
                Some(0),
                REG_SZ,
                Some(std::slice::from_raw_parts(
                    value.as_ptr().cast(),
                    value.len() * 2,
                )),
            )
            .ok()
        } else {
            let _ = RegDeleteValueW(key, &HSTRING::from("SayInput"));
            Ok(())
        };
        let _ = RegDeleteValueW(key, &HSTRING::from("VoiceTyper"));
        let _ = RegCloseKey(key);
        result?;
    }
    Ok(())
}
enum SoundCommand {
    Play(&'static [u8], Option<std::sync::mpsc::SyncSender<()>>),
    Shutdown,
}
static SOUND: std::sync::OnceLock<std::sync::Mutex<Option<std::sync::mpsc::Sender<SoundCommand>>>> =
    std::sync::OnceLock::new();
pub struct SoundRuntime(Option<std::thread::JoinHandle<()>>);
impl SoundRuntime {
    pub fn new() -> Self {
        let (tx, rx) = std::sync::mpsc::channel();
        let worker = std::thread::spawn(move || {
            while let Ok(command) = rx.recv() {
                match command {
                    SoundCommand::Play(bytes, ack) => {
                        play_sound(bytes);
                        if let Some(ack) = ack {
                            let _ = ack.send(());
                        }
                    }
                    SoundCommand::Shutdown => break,
                }
            }
        });
        *SOUND
            .get_or_init(|| std::sync::Mutex::new(None))
            .lock()
            .unwrap_or_else(|e| e.into_inner()) = Some(tx);
        Self(Some(worker))
    }
}
impl Drop for SoundRuntime {
    fn drop(&mut self) {
        if let Some(tx) = SOUND
            .get()
            .and_then(|m| m.lock().ok().and_then(|mut s| s.take()))
        {
            let _ = tx.send(SoundCommand::Shutdown);
        }
        if let Some(worker) = self.0.take() {
            let _ = worker.join();
        }
    }
}
pub fn sound(bytes: &'static [u8]) {
    let (tx, rx) = std::sync::mpsc::sync_channel(1);
    let sender = SOUND
        .get()
        .and_then(|m| m.lock().ok().and_then(|s| s.clone()));
    if let Some(sender) = sender {
        if sender.send(SoundCommand::Play(bytes, Some(tx))).is_ok() {
            let _ = rx.recv();
        }
    } else {
        play_sound(bytes);
    }
}
pub fn sound_background(bytes: &'static [u8]) {
    if let Some(sender) = SOUND
        .get()
        .and_then(|m| m.lock().ok().and_then(|s| s.clone()))
    {
        let _ = sender.send(SoundCommand::Play(bytes, None));
    }
}
fn play_sound(bytes: &'static [u8]) {
    use windows::Win32::Media::Audio::*;
    unsafe {
        let _ = PlaySoundW(
            PCWSTR(bytes.as_ptr().cast()),
            None,
            SND_MEMORY | SND_NODEFAULT | SND_SYNC,
        );
    }
}
