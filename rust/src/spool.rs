use crate::error::{AppError, AppResult};
use std::{
    fs::{self, File, OpenOptions},
    io::{Read, Seek, SeekFrom, Write},
    path::{Path, PathBuf},
    sync::{Condvar, Mutex},
    time::{SystemTime, UNIX_EPOCH},
};
struct State {
    writer: File,
    bytes: u64,
    finished: bool,
    cancelled: bool,
    committed: bool,
}
pub struct AudioSpool {
    state: Mutex<State>,
    changed: Condvar,
    path: PathBuf,
}
pub enum ReadStatus {
    Data(usize, bool),
    End,
    Cancelled,
}
impl AudioSpool {
    pub fn create(directory: &Path) -> AppResult<Self> {
        let stamp = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .map_err(|_| AppError::Storage)?
            .as_nanos();
        let path = directory.join(format!("sayinput-{stamp}-{}.wav.part", std::process::id()));
        let mut writer = OpenOptions::new()
            .read(true)
            .write(true)
            .create_new(true)
            .open(&path)?;
        writer.write_all(&header(0))?;
        Ok(Self {
            state: Mutex::new(State {
                writer,
                bytes: 0,
                finished: false,
                cancelled: false,
                committed: false,
            }),
            changed: Condvar::new(),
            path,
        })
    }
    pub fn append(&self, pcm: &[u8]) -> AppResult<()> {
        if !pcm.len().is_multiple_of(2) {
            return Err(AppError::Audio);
        }
        let mut s = self.state.lock().map_err(|_| AppError::Storage)?;
        if s.finished {
            return Err(AppError::Cancelled);
        }
        s.writer.write_all(pcm)?;
        s.bytes += pcm.len() as u64;
        self.changed.notify_all();
        Ok(())
    }
    pub fn finish(&self) -> AppResult<()> {
        let mut s = self.state.lock().map_err(|_| AppError::Storage)?;
        s.finished = true;
        self.changed.notify_all();
        Ok(())
    }
    pub fn cancel(&self) {
        if let Ok(mut s) = self.state.lock() {
            s.cancelled = true;
            self.changed.notify_all();
        }
    }
    pub fn reader(&self) -> AppResult<File> {
        // A short recording can be committed while the network handshake is
        // still pending. Serialize opening with rename and use the final path
        // if capture already stopped; each reader keeps its own file offset.
        let state = self.state.lock().map_err(|_| AppError::Storage)?;
        let path = if state.committed {
            self.path.with_extension("")
        } else {
            self.path.clone()
        };
        let mut f = File::open(path)?;
        drop(state);
        f.seek(SeekFrom::Start(44))?;
        Ok(f)
    }
    pub fn read_next(
        &self,
        reader: &mut File,
        offset: &mut u64,
        buffer: &mut [u8],
    ) -> AppResult<ReadStatus> {
        let mut s = self.state.lock().map_err(|_| AppError::Storage)?;
        while !s.cancelled && !s.finished && s.bytes - *offset < buffer.len() as u64 {
            s = self.changed.wait(s).map_err(|_| AppError::Storage)?;
        }
        if s.cancelled {
            return Ok(ReadStatus::Cancelled);
        }
        let count = (s.bytes - *offset).min(buffer.len() as u64) as usize;
        if count == 0 {
            return Ok(ReadStatus::End);
        }
        let last = s.finished && *offset + count as u64 == s.bytes;
        drop(s);
        reader.read_exact(&mut buffer[..count])?;
        *offset += count as u64;
        Ok(ReadStatus::Data(count, last))
    }
    pub fn commit(&self) -> AppResult<Option<PathBuf>> {
        let mut s = self.state.lock().map_err(|_| AppError::Storage)?;
        if !s.finished {
            return Err(AppError::Cancelled);
        }
        if s.bytes == 0 {
            return Ok(None);
        }
        let bytes = u32::try_from(s.bytes).map_err(|_| AppError::Storage)?;
        s.writer.set_len(44 + u64::from(bytes))?;
        s.writer.seek(SeekFrom::Start(0))?;
        s.writer.write_all(&header(bytes))?;
        s.writer.flush()?;
        let target = self.path.with_extension("");
        fs::rename(&self.path, &target)?;
        s.committed = true;
        Ok(Some(target))
    }
}
impl Drop for AudioSpool {
    fn drop(&mut self) {
        let _ = fs::remove_file(&self.path);
    }
}
fn header(bytes: u32) -> [u8; 44] {
    let mut h = [0u8; 44];
    h[..4].copy_from_slice(b"RIFF");
    h[4..8].copy_from_slice(&(bytes + 36).to_le_bytes());
    h[8..16].copy_from_slice(b"WAVEfmt ");
    h[16..20].copy_from_slice(&16u32.to_le_bytes());
    h[20..22].copy_from_slice(&1u16.to_le_bytes());
    h[22..24].copy_from_slice(&1u16.to_le_bytes());
    h[24..28].copy_from_slice(&16000u32.to_le_bytes());
    h[28..32].copy_from_slice(&32000u32.to_le_bytes());
    h[32..34].copy_from_slice(&2u16.to_le_bytes());
    h[34..36].copy_from_slice(&16u16.to_le_bytes());
    h[36..40].copy_from_slice(b"data");
    h[40..44].copy_from_slice(&bytes.to_le_bytes());
    h
}
pub fn prune(directory: &Path) -> AppResult<()> {
    let mut files = fs::read_dir(directory)?
        .filter_map(Result::ok)
        .filter(|f| f.path().extension().is_some_and(|s| s == "wav"))
        .collect::<Vec<_>>();
    files.sort_by_key(|f| f.metadata().and_then(|m| m.modified()).ok());
    let remove = files.len().saturating_sub(20);
    for file in files.into_iter().take(remove) {
        fs::remove_file(file.path())?;
    }
    Ok(())
}
