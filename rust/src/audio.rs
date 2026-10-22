use crate::{
    error::{AppError, AppResult},
    platform::{Com, Event},
    spool::AudioSpool,
};
use std::{
    sync::{Arc, mpsc},
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
use windows::{
    Win32::{
        Devices::FunctionDiscovery::PKEY_Device_FriendlyName,
        Media::Audio::{Endpoints::IAudioEndpointVolume, *},
        System::{
            Com::{CLSCTX_ALL, CoCreateInstance, CoTaskMemFree, STGM_READ},
            Threading::WaitForMultipleObjects,
        },
    },
    core::HSTRING,
};
#[derive(Clone)]
pub struct AudioDevice {
    pub id: String,
    pub name: String,
}
pub fn warmup(open_device: bool, cancel: Arc<Event>) {
    if unsafe { windows::Win32::System::Threading::WaitForSingleObject(cancel.0, 1500) }.0 == 0 {
        return;
    }
    if open_device {
        if let Ok(capture) = Capture::start(None, None, Arc::new(|_| {})) {
            unsafe {
                windows::Win32::System::Threading::WaitForSingleObject(cancel.0, 80);
            }
            let _ = capture.stop();
        }
    } else if !cancel.signaled() {
        let _ = (|| -> AppResult<()> {
            let _com = Com::new()?;
            unsafe {
                let enumerator: IMMDeviceEnumerator =
                    CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
                let device = enumerator.GetDefaultAudioEndpoint(eCapture, eConsole)?;
                let _client: IAudioClient = device.Activate(CLSCTX_ALL, None)?;
            }
            Ok(())
        })();
    }
}
pub fn devices() -> AppResult<Vec<AudioDevice>> {
    let _com = Com::new()?;
    unsafe {
        let enumerator: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
        let collection = enumerator.EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE)?;
        let mut result = Vec::new();
        for n in 0..collection.GetCount()? {
            let device = collection.Item(n)?;
            let id = device.GetId()?;
            let text = id.to_string().map_err(|_| AppError::Audio)?;
            CoTaskMemFree(Some(id.0.cast()));
            let prop = device
                .OpenPropertyStore(STGM_READ)?
                .GetValue(&PKEY_Device_FriendlyName)?;
            let name = prop.to_string();
            result.push(AudioDevice { id: text, name });
        }
        Ok(result)
    }
}
pub struct MuteGuard(Vec<(IAudioEndpointVolume, bool)>);
impl MuteGuard {
    pub fn acquire() -> AppResult<Self> {
        unsafe {
            let enumerator: IMMDeviceEnumerator =
                CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
            let collection = enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE)?;
            let mut guard = Self(Vec::new());
            for i in 0..collection.GetCount()? {
                let endpoint: IAudioEndpointVolume =
                    collection.Item(i)?.Activate(CLSCTX_ALL, None)?;
                let old = endpoint.GetMute()?.as_bool();
                guard.0.push((endpoint, old));
                guard.0.last().unwrap().0.SetMute(true, std::ptr::null())?;
            }
            Ok(guard)
        }
    }
    pub fn restore(&mut self) {
        for (endpoint, old) in self.0.drain(..) {
            unsafe {
                let _ = endpoint.SetMute(old, std::ptr::null());
            }
        }
    }
}
impl Drop for MuteGuard {
    fn drop(&mut self) {
        self.restore();
    }
}
pub struct Capture {
    stop: Arc<Event>,
    thread: Option<JoinHandle<AppResult<()>>>,
}
impl Capture {
    pub fn start(
        device: Option<String>,
        spool: Option<Arc<AudioSpool>>,
        level: Arc<dyn Fn(f32) + Send + Sync>,
    ) -> AppResult<Self> {
        let stop = Arc::new(Event::new(true)?);
        let signal = stop.clone();
        let (tx, rx) = mpsc::sync_channel(1);
        let thread = thread::spawn(move || {
            let result = capture_loop(device, spool, level, signal, &tx);
            if result.is_err() {
                let _ = tx.try_send(Err(AppError::Audio));
            }
            result
        });
        match rx.recv_timeout(Duration::from_secs(10)) {
            Ok(Ok(())) => Ok(Self {
                stop,
                thread: Some(thread),
            }),
            _ => {
                stop.set();
                let _ = thread.join();
                Err(AppError::Audio)
            }
        }
    }
    pub fn stop(mut self) -> AppResult<()> {
        self.stop.set();
        self.thread
            .take()
            .unwrap()
            .join()
            .map_err(|_| AppError::Audio)?
    }
    pub fn finished(&self) -> bool {
        self.thread.as_ref().is_none_or(|t| t.is_finished())
    }
}
impl Drop for Capture {
    fn drop(&mut self) {
        self.stop.set();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
fn capture_loop(
    device: Option<String>,
    spool: Option<Arc<AudioSpool>>,
    level: Arc<dyn Fn(f32) + Send + Sync>,
    stop: Arc<Event>,
    ready: &mpsc::SyncSender<AppResult<()>>,
) -> AppResult<()> {
    let _com = Com::new()?;
    unsafe {
        let enumerator: IMMDeviceEnumerator =
            CoCreateInstance(&MMDeviceEnumerator, None, CLSCTX_ALL)?;
        let device = if let Some(id) = device {
            enumerator.GetDevice(&HSTRING::from(id))?
        } else {
            enumerator.GetDefaultAudioEndpoint(eCapture, eConsole)?
        };
        let client: IAudioClient = device.Activate(CLSCTX_ALL, None)?;
        let format = WAVEFORMATEX {
            wFormatTag: 1,
            nChannels: 1,
            nSamplesPerSec: 16000,
            nAvgBytesPerSec: 32000,
            nBlockAlign: 2,
            wBitsPerSample: 16,
            cbSize: 0,
        };
        client
            .Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                    | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM
                    | AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY,
                1_000_000,
                0,
                &format,
                None,
            )
            .map_err(|_| AppError::Audio)?;
        let data = Event::new(false)?;
        client.SetEventHandle(data.0)?;
        let capture: IAudioCaptureClient = client.GetService()?;
        client.Start()?;
        let _ = ready.send(Ok(()));
        let mut last = Instant::now() - Duration::from_secs(1);
        let mut silence = Vec::new();
        let result = (|| -> AppResult<()> {
            loop {
                let event = WaitForMultipleObjects(&[stop.0, data.0], false, 1000);
                let stopping = event.0 == 0;
                if stopping {
                    client.Stop()?;
                }
                if event.0 == u32::MAX {
                    return Err(AppError::Audio);
                }
                while capture.GetNextPacketSize()? > 0 {
                    let (mut ptr, mut frames, mut flags) = (std::ptr::null_mut(), 0, 0);
                    capture.GetBuffer(&mut ptr, &mut frames, &mut flags, None, None)?;
                    let bytes = frames as usize * 2;
                    let pcm = if flags & AUDCLNT_BUFFERFLAGS_SILENT.0 as u32 != 0 {
                        silence.resize(bytes, 0);
                        &silence[..bytes]
                    } else {
                        std::slice::from_raw_parts(ptr, bytes)
                    };
                    let append = if let Some(ref spool) = spool {
                        spool.append(pcm)
                    } else {
                        Ok(())
                    };
                    if last.elapsed() >= Duration::from_millis(34) {
                        let peak = pcm
                            .as_chunks::<2>()
                            .0
                            .iter()
                            .map(|p| {
                                i16::from_le_bytes([p[0], p[1]]).unsigned_abs() as f32 / 32768.0
                            })
                            .fold(0.0, f32::max);
                        level(peak);
                        last = Instant::now();
                    }
                    capture.ReleaseBuffer(frames)?;
                    append?;
                }
                if stopping {
                    break;
                }
            }
            Ok(())
        })();
        let _ = client.Stop();
        result
    }
}
