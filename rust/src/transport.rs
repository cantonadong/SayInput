use crate::{
    error::{AppError, AppResult},
    platform::wide,
    speech::MAX_FRAME,
    storage::Provider,
};
use std::{
    collections::VecDeque,
    ffi::c_void,
    sync::{
        Arc, Condvar, Mutex,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};
use windows::{Win32::Networking::WinHttp::*, core::PCWSTR};
pub type Cancel = Arc<AtomicBool>;
struct Notice {
    kind: u32,
    bytes: u32,
    buffer: WINHTTP_WEB_SOCKET_BUFFER_TYPE,
}
struct Notifications {
    queue: Mutex<VecDeque<Notice>>,
    wake: Condvar,
    closed: AtomicBool,
}
unsafe extern "system" fn callback(
    _: *mut c_void,
    context: usize,
    status: u32,
    info: *mut c_void,
    len: u32,
) {
    if context == 0 {
        return;
    }
    // A dedicated strong reference lives from attach until HANDLE_CLOSING.
    let state = unsafe { &*(context as *const Notifications) };
    if status == WINHTTP_CALLBACK_STATUS_HANDLE_CLOSING {
        // Predicate and notification share the waiter's mutex, so the final
        // callback cannot race between checking closed and entering wait.
        {
            let _guard = state.queue.lock().unwrap_or_else(|e| e.into_inner());
            state.closed.store(true, Ordering::Release);
            state.wake.notify_all();
        }
        unsafe {
            drop(Arc::from_raw(context as *const Notifications));
        }
        return;
    }
    if ![
        WINHTTP_CALLBACK_STATUS_SENDREQUEST_COMPLETE,
        WINHTTP_CALLBACK_STATUS_HEADERS_AVAILABLE,
        WINHTTP_CALLBACK_STATUS_READ_COMPLETE,
        WINHTTP_CALLBACK_STATUS_WRITE_COMPLETE,
        WINHTTP_CALLBACK_STATUS_REQUEST_ERROR,
    ]
    .contains(&status)
    {
        return;
    }
    let (mut bytes, mut buffer) = (0, WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE);
    if status == WINHTTP_CALLBACK_STATUS_READ_COMPLETE
        && !info.is_null()
        && len as usize >= std::mem::size_of::<WINHTTP_WEB_SOCKET_STATUS>()
    {
        let data = unsafe { &*info.cast::<WINHTTP_WEB_SOCKET_STATUS>() };
        bytes = data.dwBytesTransferred;
        buffer = data.eBufferType;
    }
    if let Ok(mut q) = state.queue.lock() {
        q.push_back(Notice {
            kind: status,
            bytes,
            buffer,
        });
        state.wake.notify_all();
    }
}
struct Internet(*mut c_void);
impl Drop for Internet {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe {
                let _ = WinHttpCloseHandle(self.0);
            }
        }
    }
}
pub struct AsyncHandle {
    handle: Mutex<usize>,
    state: Arc<Notifications>,
}
impl AsyncHandle {
    unsafe fn upgrade(request: *mut c_void) -> AppResult<Self> {
        let state = Arc::new(Notifications {
            queue: Mutex::new(VecDeque::new()),
            wake: Condvar::new(),
            closed: AtomicBool::new(false),
        });
        let context = Arc::into_raw(state.clone()) as usize;
        // WebSocket operations use the context supplied at upgrade time.
        let raw = unsafe { WinHttpWebSocketCompleteUpgrade(request, Some(context)) };
        if raw.is_null() {
            unsafe {
                drop(Arc::from_raw(context as *const Notifications));
            }
            return Err(AppError::Network);
        }
        unsafe {
            WinHttpSetStatusCallback(
                raw,
                Some(callback),
                WINHTTP_CALLBACK_FLAG_ALL_NOTIFICATIONS,
                0,
            );
        }
        Ok(Self {
            handle: Mutex::new(raw as usize),
            state,
        })
    }
    unsafe fn attach(raw: *mut c_void) -> AppResult<Self> {
        if raw.is_null() {
            return Err(AppError::Network);
        }
        let guard = Internet(raw);
        let state = Arc::new(Notifications {
            queue: Mutex::new(VecDeque::new()),
            wake: Condvar::new(),
            closed: AtomicBool::new(false),
        });
        let context = Arc::into_raw(state.clone()) as usize;
        unsafe {
            WinHttpSetStatusCallback(
                raw,
                Some(callback),
                WINHTTP_CALLBACK_FLAG_ALL_NOTIFICATIONS,
                0,
            );
        }
        if unsafe {
            WinHttpSetOption(
                Some(raw),
                WINHTTP_OPTION_CONTEXT_VALUE,
                Some(&context.to_ne_bytes()),
            )
        }
        .is_err()
        {
            unsafe {
                drop(Arc::from_raw(context as *const Notifications));
            }
            return Err(AppError::Network);
        }
        std::mem::forget(guard);
        Ok(Self {
            handle: Mutex::new(raw as usize),
            state,
        })
    }
    fn call<T>(&self, f: impl FnOnce(*mut c_void) -> AppResult<T>) -> AppResult<T> {
        let guard = self.handle.lock().map_err(|_| AppError::Network)?;
        if *guard == 0 {
            return Err(AppError::Cancelled);
        }
        f(*guard as *mut c_void)
    }
    pub fn close(&self) {
        if let Ok(mut h) = self.handle.lock()
            && *h != 0
        {
            let raw = *h;
            *h = 0;
            unsafe {
                let _ = WinHttpCloseHandle(raw as *mut c_void);
            }
        }
    }
    fn wait(&self, kind: u32, cancel: &Cancel, deadline: Instant) -> AppResult<Notice> {
        self.wait_clock(kind, cancel, deadline, None)
    }
    fn wait_clock(
        &self,
        kind: u32,
        cancel: &Cancel,
        deadline: Instant,
        clock: Option<&Mutex<Option<Instant>>>,
    ) -> AppResult<Notice> {
        let mut q = self.state.queue.lock().map_err(|_| AppError::Network)?;
        loop {
            if cancel.load(Ordering::Acquire) {
                drop(q);
                self.close();
                self.wait_closed();
                return Err(AppError::Cancelled);
            }
            if let Some(i) = q
                .iter()
                .position(|n| n.kind == kind || n.kind == WINHTTP_CALLBACK_STATUS_REQUEST_ERROR)
            {
                let n = q.remove(i).unwrap();
                if n.kind == kind {
                    return Ok(n);
                }
                // REQUEST_ERROR can belong to the other WebSocket direction.
                // Close both operations and await final native teardown before
                // either caller may release a still-pending send/read buffer.
                drop(q);
                self.close();
                self.wait_closed();
                return Err(AppError::Network);
            }
            if self.state.closed.load(Ordering::Acquire) {
                return Err(AppError::Cancelled);
            }
            let deadline = clock
                .and_then(|c| c.lock().ok().and_then(|t| *t))
                .map_or(deadline, |t| (t + Duration::from_secs(15)).min(deadline));
            let now = Instant::now();
            if now >= deadline {
                drop(q);
                self.close();
                self.wait_closed();
                return Err(AppError::Timeout);
            }
            q = self
                .state
                .wake
                .wait_timeout(q, Duration::from_millis(50).min(deadline - now))
                .map_err(|_| AppError::Network)?
                .0;
        }
    }
    fn wait_closed(&self) {
        let mut q = self.state.queue.lock().unwrap_or_else(|e| e.into_inner());
        while !self.state.closed.load(Ordering::Acquire) {
            q = self.state.wake.wait(q).unwrap_or_else(|e| e.into_inner());
        }
    }
}
impl Drop for AsyncHandle {
    fn drop(&mut self) {
        self.close();
        self.wait_closed();
    }
}
pub struct SpeechConnection {
    socket: Arc<AsyncHandle>,
    _connection: Internet,
    _session: Internet,
}
// Parent handles stay alive until the final socket owner is joined and dropped.
unsafe impl Send for SpeechConnection {}
impl SpeechConnection {
    pub fn connect(provider: &Provider, cancel: &Cancel) -> AppResult<Self> {
        provider.validate()?;
        unsafe {
            let session = Internet(WinHttpOpen(
                PCWSTR(wide("SayInput/1.5 Rust").as_ptr()),
                WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                PCWSTR::null(),
                PCWSTR::null(),
                WINHTTP_FLAG_ASYNC,
            ));
            if session.0.is_null() {
                return Err(AppError::Network);
            }
            WinHttpSetTimeouts(session.0, 10000, 10000, 10000, 10000)
                .map_err(|_| AppError::Network)?;
            let connection = Internet(WinHttpConnect(
                session.0,
                PCWSTR(wide("openspeech.bytedance.com").as_ptr()),
                443,
                0,
            ));
            if connection.0.is_null() {
                return Err(AppError::Network);
            }
            let raw = WinHttpOpenRequest(
                connection.0,
                PCWSTR(wide("GET").as_ptr()),
                PCWSTR(wide("/api/v3/sauc/bigmodel_async").as_ptr()),
                PCWSTR::null(),
                PCWSTR::null(),
                std::ptr::null(),
                WINHTTP_FLAG_SECURE,
            );
            let request = AsyncHandle::attach(raw)?;
            let guid = windows::Win32::System::Com::CoCreateGuid()?;
            let id = format!("{guid:?}");
            let auth = if !provider.api_key.trim().is_empty() {
                format!("X-Api-Key: {}\r\n", provider.api_key)
            } else {
                format!(
                    "X-Api-App-Key: {}\r\nX-Api-Access-Key: {}\r\n",
                    provider.app_id, provider.access_token
                )
            };
            let headers = zeroize::Zeroizing::new(wide(&format!(
                "{auth}X-Api-Resource-Id: {}\r\nX-Api-Request-Id: {id}\r\nX-Api-Connect-Id: {id}\r\nX-Api-Sequence: -1\r\n",
                provider.resource_id
            )));
            let deadline = Instant::now() + Duration::from_secs(10);
            request.call(|h| {
                WinHttpSetOption(Some(h), WINHTTP_OPTION_UPGRADE_TO_WEB_SOCKET, None)
                    .map_err(|_| AppError::Network)?;
                WinHttpSendRequest(
                    h,
                    Some(&headers[..headers.len() - 1]),
                    None,
                    0,
                    0,
                    Arc::as_ptr(&request.state) as usize,
                )
                .map_err(|_| AppError::Network)
            })?;
            request.wait(
                WINHTTP_CALLBACK_STATUS_SENDREQUEST_COMPLETE,
                cancel,
                deadline,
            )?;
            request.call(|h| {
                WinHttpReceiveResponse(h, std::ptr::null_mut()).map_err(|_| AppError::Network)
            })?;
            request.wait(WINHTTP_CALLBACK_STATUS_HEADERS_AVAILABLE, cancel, deadline)?;
            let socket = request.call(|h| {
                let (mut status, mut bytes, mut index) = (0u32, 4u32, 0u32);
                WinHttpQueryHeaders(
                    h,
                    WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                    PCWSTR::null(),
                    Some((&mut status as *mut u32).cast()),
                    &mut bytes,
                    &mut index,
                )
                .map_err(|_| AppError::Network)?;
                if status != 101 {
                    return Err(AppError::Service(status));
                }
                AsyncHandle::upgrade(h)
            })?;
            Ok(Self {
                socket: Arc::new(socket),
                _connection: connection,
                _session: session,
            })
        }
    }
    pub fn socket(&self) -> Arc<AsyncHandle> {
        self.socket.clone()
    }
}
impl AsyncHandle {
    pub fn send(&self, bytes: &[u8], cancel: &Cancel, deadline: Instant) -> AppResult<()> {
        self.call(|h| {
            let code = unsafe {
                WinHttpWebSocketSend(
                    h,
                    WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE,
                    Some(bytes),
                )
            };
            if code == 0 || code == 997 {
                Ok(())
            } else {
                Err(AppError::Network)
            }
        })?;
        self.wait(WINHTTP_CALLBACK_STATUS_WRITE_COMPLETE, cancel, deadline)?;
        Ok(())
    }
    pub fn receive(&self, cancel: &Cancel, deadline: Instant) -> AppResult<Vec<u8>> {
        self.receive_inner(cancel, deadline, None)
    }
    pub fn receive_with_clock(
        &self,
        cancel: &Cancel,
        deadline: Instant,
        clock: &Mutex<Option<Instant>>,
    ) -> AppResult<Vec<u8>> {
        self.receive_inner(cancel, deadline, Some(clock))
    }
    fn receive_inner(
        &self,
        cancel: &Cancel,
        deadline: Instant,
        clock: Option<&Mutex<Option<Instant>>>,
    ) -> AppResult<Vec<u8>> {
        let mut packet = Vec::new();
        let mut buffer = vec![0u8; 8192];
        loop {
            self.call(|h| {
                let code = unsafe {
                    WinHttpWebSocketReceive(
                        h,
                        buffer.as_mut_ptr().cast(),
                        buffer.len() as u32,
                        std::ptr::null_mut(),
                        std::ptr::null_mut(),
                    )
                };
                if code == 0 || code == 997 {
                    Ok(())
                } else {
                    Err(AppError::Network)
                }
            })?;
            let status = self.wait_clock(
                WINHTTP_CALLBACK_STATUS_READ_COMPLETE,
                cancel,
                deadline,
                clock,
            )?;
            if status.buffer != WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE
                && status.buffer != WINHTTP_WEB_SOCKET_BINARY_FRAGMENT_BUFFER_TYPE
            {
                return Err(AppError::Protocol);
            }
            if status.bytes as usize > buffer.len()
                || packet.len() + status.bytes as usize > MAX_FRAME
            {
                return Err(AppError::Protocol);
            }
            packet.extend_from_slice(&buffer[..status.bytes as usize]);
            if status.buffer == WINHTTP_WEB_SOCKET_BINARY_MESSAGE_BUFFER_TYPE {
                return Ok(packet);
            }
        }
    }
}
