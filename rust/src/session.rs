use crate::{
    audio::{Capture, MuteGuard},
    error::{AppError, AppResult},
    input::{self, Target},
    platform::{self, Com},
    speech,
    spool::{AudioSpool, ReadStatus},
    storage::{PortablePaths, Provider},
    transport::{Cancel, SpeechConnection},
};
use std::{
    sync::{
        Arc, Mutex,
        atomic::{AtomicBool, Ordering},
        mpsc,
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
pub const START: &[u8] = include_bytes!("../../resources/sounds/start.wav");
pub const STOP: &[u8] = include_bytes!("../../resources/sounds/stop.wav");
pub const SUCCESS: &[u8] = include_bytes!("../../resources/sounds/success.wav");
pub const ERROR: &[u8] = include_bytes!("../../resources/sounds/error.wav");
#[derive(Clone, Copy, PartialEq)]
pub enum State {
    Starting,
    Recording,
    Finalizing,
    Injecting,
    Idle,
}
pub enum Notice {
    State(u64, State),
    Partial(u64, String),
    Level(u64, f32),
    LastText(u64, String),
    Done(u64, AppResult<()>),
    TestDone(AppResult<()>),
    CopyDone(AppResult<()>),
    Saved(AppResult<()>),
}
#[derive(Clone)]
pub struct Sink {
    hwnd: usize,
    queue: Arc<Mutex<Vec<Notice>>>,
}
pub const NOTIFY: u32 = 0x8000 + 10;
impl Sink {
    pub fn new(hwnd: usize) -> Self {
        Self {
            hwnd,
            queue: Arc::new(Mutex::new(Vec::new())),
        }
    }
    pub fn post(&self, event: Notice) {
        if let Ok(mut q) = self.queue.lock() {
            match &event {
                Notice::Level(id, _) => q.retain(|n| !matches!(n,Notice::Level(i,_) if i==id)),
                Notice::Partial(id, _) => q.retain(|n| !matches!(n,Notice::Partial(i,_) if i==id)),
                _ => {}
            }
            let empty = q.is_empty();
            q.push(event);
            if empty {
                unsafe {
                    let _ = windows::Win32::UI::WindowsAndMessaging::PostMessageW(
                        Some(windows::Win32::Foundation::HWND(self.hwnd as *mut _)),
                        NOTIFY,
                        windows::Win32::Foundation::WPARAM(0),
                        windows::Win32::Foundation::LPARAM(0),
                    );
                }
            }
        }
    }
    pub fn drain(&self) -> Vec<Notice> {
        self.queue
            .lock()
            .map(|mut q| std::mem::take(&mut *q))
            .unwrap_or_default()
    }
}
pub enum Command {
    Stop,
    Cancel,
}
pub struct Session {
    tx: mpsc::Sender<Command>,
    cancel: Cancel,
    thread: Option<JoinHandle<()>>,
}
impl Session {
    pub fn start(
        id: u64,
        target: Target,
        provider: Provider,
        device: Option<String>,
        paths: PortablePaths,
        sink: Sink,
    ) -> Self {
        let (tx, rx) = mpsc::channel();
        let cancel = Arc::new(AtomicBool::new(false));
        let flag = cancel.clone();
        let thread = thread::spawn(move || {
            let log = paths.data.join("last-session.log");
            let started = Instant::now();
            let _ = std::fs::write(&log, "session-start\n");
            let result = run(id, target, provider, device, paths, &sink, flag, rx);
            record_timing(&log, started, &format!("session-result {result:?}"));
            if matches!(result, Err(AppError::Cancelled)) {
                sink.post(Notice::Done(id, Ok(())));
            } else {
                if result.is_err() {
                    platform::sound_background(ERROR);
                }
                sink.post(Notice::Done(id, result));
            }
        });
        Self {
            tx,
            cancel,
            thread: Some(thread),
        }
    }
    pub fn stop(&self) {
        let _ = self.tx.send(Command::Stop);
    }
    pub fn cancel(&self) {
        self.cancel.store(true, Ordering::Release);
        let _ = self.tx.send(Command::Cancel);
    }
    pub fn join(mut self) {
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
impl Drop for Session {
    fn drop(&mut self) {
        self.cancel();
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
    }
}
#[expect(
    clippy::too_many_arguments,
    reason = "One-shot session worker takes owned session dependencies"
)]
fn run(
    id: u64,
    target: Target,
    provider: Provider,
    device: Option<String>,
    paths: PortablePaths,
    sink: &Sink,
    cancel: Cancel,
    commands: mpsc::Receiver<Command>,
) -> AppResult<()> {
    let log = paths.data.join("last-session.log");
    let timing = Instant::now();
    let _com = Com::new()?;
    let spool = Arc::new(AudioSpool::create(&paths.recordings)?);
    let events = sink.clone();
    let deadline = Instant::now() + Duration::from_secs(720);
    let send_spool = spool.clone();
    let send_cancel = cancel.clone();
    let send_sink = sink.clone();
    let send_log = log.clone();
    let (net_tx, net_rx) = mpsc::sync_channel(1);
    let network = thread::spawn(move || {
        let result = recognize(
            provider,
            send_spool.clone(),
            send_cancel,
            deadline,
            Some((id, send_sink)),
            move |stage| record_timing(&send_log, timing, stage),
        );
        if result.is_err() {
            send_spool.cancel();
        }
        let _ = net_tx.send(result);
    });
    // Every path after spawning network must join it before resources are dropped.
    let capture = match Capture::start(
        device,
        Some(spool.clone()),
        Arc::new(move |v| events.post(Notice::Level(id, v))),
    ) {
        Ok(capture) => capture,
        Err(error) => {
            cancel.store(true, Ordering::Release);
            spool.cancel();
            let _ = network.join();
            return Err(error);
        }
    };
    let started = Instant::now();
    record_timing(&log, timing, "capture-ready");
    sink.post(Notice::State(id, State::Recording));
    let mut mute = None;
    let record_result = (|| -> AppResult<()> {
        platform::sound(START);
        if cancel.load(Ordering::Acquire) {
            return Err(AppError::Cancelled);
        }
        mute = Some(MuteGuard::acquire()?);
        loop {
            if cancel.load(Ordering::Acquire) {
                return Err(AppError::Cancelled);
            }
            if capture.finished() {
                return Err(AppError::Audio);
            }
            if let Ok(result) = net_rx.try_recv() {
                return result.and(Err(AppError::Protocol));
            }
            if started.elapsed() >= Duration::from_secs(600) {
                break;
            }
            match commands.recv_timeout(Duration::from_millis(40)) {
                Ok(Command::Stop) => break,
                Ok(Command::Cancel) | Err(mpsc::RecvTimeoutError::Disconnected) => {
                    return Err(AppError::Cancelled);
                }
                Err(mpsc::RecvTimeoutError::Timeout) => {}
            }
        }
        Ok(())
    })();
    record_timing(&log, timing, "stop-requested");
    let stopped = capture.stop();
    record_timing(&log, timing, "capture-stopped");
    let outcome = match stopped {
        Err(error) => Err(error),
        Ok(()) => record_result,
    }
    .and_then(|_| spool.finish());
    if outcome.is_ok() {
        sink.post(Notice::State(id, State::Finalizing));
    }
    // Let the sender finish while output devices restore their mute states.
    drop(mute);
    if outcome.is_err() {
        let preserve = !matches!(outcome, Err(AppError::Cancelled));
        cancel.store(true, Ordering::Release);
        spool.cancel();
        let _ = network.join();
        if preserve {
            let _ = spool
                .finish()
                .and_then(|_| spool.commit())
                .and_then(|_| crate::spool::prune(&paths.recordings));
        }
        return outcome;
    }
    // Wake the sender before playing feedback; finalization proceeds in parallel.
    platform::sound_background(STOP);
    // Normal capture completion commits independently of cloud success.
    let archive_result = spool
        .commit()
        .and_then(|_| crate::spool::prune(&paths.recordings));
    record_timing(&log, timing, &format!("archive {archive_result:?}"));
    let final_result = loop {
        if cancel.load(Ordering::Acquire) {
            spool.cancel();
            break Err(AppError::Cancelled);
        }
        if Instant::now() >= deadline {
            cancel.store(true, Ordering::Release);
            spool.cancel();
            break Err(AppError::Timeout);
        }
        match net_rx.recv_timeout(Duration::from_millis(40)) {
            Ok(r) => break r,
            Err(mpsc::RecvTimeoutError::Disconnected) => break Err(AppError::Network),
            Err(mpsc::RecvTimeoutError::Timeout) => {}
        }
    };
    let _ = network.join();
    let (text, _connection) = final_result?;
    record_timing(&log, timing, "final-received");
    if cancel.load(Ordering::Acquire) {
        return Err(AppError::Cancelled);
    }
    if !text.trim().is_empty() {
        sink.post(Notice::LastText(id, text.clone()));
    }
    if text.trim().is_empty() {
        return archive_result;
    }
    sink.post(Notice::State(id, State::Injecting));
    record_timing(&log, timing, "input-start");
    input::inject(target, &text, &cancel)?;
    record_timing(&log, timing, "input-completed");
    archive_result?;
    platform::sound_background(SUCCESS);
    Ok(())
}
fn recognize(
    provider: Provider,
    spool: Arc<AudioSpool>,
    cancel: Cancel,
    deadline: Instant,
    sink: Option<(u64, Sink)>,
    progress: impl Fn(&str),
) -> AppResult<(String, SpeechConnection)> {
    let _com = Com::new()?;
    progress("network-connect-start");
    let connection = SpeechConnection::connect(&provider, &cancel)?;
    progress("network-connected");
    let socket = connection.socket();
    socket.send(
        &speech::encode_configuration()?,
        &cancel,
        Instant::now() + Duration::from_secs(10),
    )?;
    progress("configuration-sent");
    let ack = speech::decode(&socket.receive(&cancel, Instant::now() + Duration::from_secs(10))?)?;
    progress("configuration-acknowledged");
    if let Some(code) = ack.error_code {
        return Err(AppError::Service(code));
    }
    if ack.is_final {
        return Err(AppError::Protocol);
    }
    let completing = Arc::new(AtomicBool::new(false));
    let final_started = Arc::new(Mutex::new(None::<Instant>));
    let receiver_socket = socket.clone();
    let receiver_cancel = cancel.clone();
    let receiver_completing = completing.clone();
    let receive_spool = spool.clone();
    let clock = final_started.clone();
    let receiver = thread::spawn(move || {
        let result = (|| -> AppResult<String> {
            let mut last = Instant::now() - Duration::from_secs(1);
            loop {
                let receive_deadline = clock
                    .lock()
                    .ok()
                    .and_then(|v| *v)
                    .map_or(deadline, |t| t + Duration::from_secs(15))
                    .min(deadline);
                // Receive waits also observe the shared final deadline while active.
                let packet = receiver_socket.receive_with_clock(
                    &receiver_cancel,
                    receive_deadline,
                    &clock,
                )?;
                let response = speech::decode(&packet)?;
                if let Some(code) = response.error_code {
                    return Err(AppError::Service(code));
                }
                if response.is_final {
                    if !receiver_completing.load(Ordering::Acquire) {
                        return Err(AppError::Protocol);
                    }
                    return Ok(response.text);
                }
                if last.elapsed() >= Duration::from_millis(50) {
                    if let Some((id, ref sink)) = sink {
                        sink.post(Notice::Partial(id, response.text));
                    }
                    last = Instant::now();
                }
            }
        })();
        if result.is_err() {
            receive_spool.cancel();
        }
        result
    });
    let send_result = (|| -> AppResult<()> {
        let mut reader = spool.reader()?;
        let mut offset = 0;
        let mut packet = [0u8; 6400];
        loop {
            if cancel.load(Ordering::Acquire) {
                return Err(AppError::Cancelled);
            }
            match spool.read_next(&mut reader, &mut offset, &mut packet)? {
                ReadStatus::Data(n, last) => {
                    if last {
                        completing.store(true, Ordering::Release);
                    }
                    socket.send(
                        &speech::encode_audio(&packet[..n], last, None)?,
                        &cancel,
                        deadline,
                    )?;
                    if last {
                        *final_started.lock().map_err(|_| AppError::Network)? =
                            Some(Instant::now());
                        return Ok(());
                    }
                }
                ReadStatus::End => break,
                ReadStatus::Cancelled => return Err(AppError::Cancelled),
            }
        }
        completing.store(true, Ordering::Release);
        socket.send(&speech::encode_audio(&[], true, None)?, &cancel, deadline)?;
        *final_started.lock().map_err(|_| AppError::Network)? = Some(Instant::now());
        Ok(())
    })();
    if send_result.is_err() {
        socket.close();
    }
    progress(if send_result.is_ok() {
        "audio-final-sent"
    } else {
        "audio-send-failed"
    });
    let received = receiver.join().map_err(|_| AppError::Network)?;
    progress(if received.is_ok() {
        "receive-completed"
    } else {
        "receive-failed"
    });
    // Prefer the receiver's actual failure over the spool cancellation it triggers.
    match received {
        Err(error) => Err(error),
        Ok(text) => {
            send_result?;
            Ok((text, connection))
        }
    }
}
fn record_timing(path: &std::path::Path, started: Instant, stage: &str) {
    use std::io::Write;
    if let Ok(mut file) = std::fs::OpenOptions::new().append(true).open(path) {
        let _ = writeln!(file, "{}ms {stage}", started.elapsed().as_millis());
    }
}
pub fn connection_test(provider: Provider, cancel: Cancel) -> AppResult<()> {
    let temp = std::env::temp_dir();
    let spool = Arc::new(AudioSpool::create(&temp)?);
    spool.append(&[0u8; 32000])?;
    spool.finish()?;
    recognize(
        provider,
        spool,
        cancel,
        Instant::now() + Duration::from_secs(30),
        None,
        |_| {},
    )
    .map(|_| ())
}
