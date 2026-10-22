#![allow(unsafe_op_in_unsafe_fn)]
use crate::{
    audio::{self, AudioDevice, Capture},
    error::{AppError, AppResult},
    hotkey::{self, Hook},
    i18n::Language,
    input::{self, Target},
    platform::{self, wide},
    session::{self, Notice, Session, Sink, State},
    storage::{Configuration, PortablePaths},
    transport::Cancel,
};
use std::{
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    thread::{self, JoinHandle},
    time::{Duration, Instant},
};
use windows::Win32::UI::HiDpi::*;
use windows::{
    Win32::{
        Foundation::*,
        Graphics::Gdi::*,
        System::{LibraryLoader::GetModuleHandleW, Threading::CreateMutexW},
        UI::{Controls::*, Input::KeyboardAndMouse::*, Shell::*, WindowsAndMessaging::*},
    },
    core::{PCWSTR, w},
};
const TRAY: u32 = 0x8000 + 1;
const ACTIVATE: u32 = 0x8000 + 2;
const SAVE: u16 = 100;
const MINIMIZE: u16 = 101;
const EXIT: u16 = 102;
const EDIT: u16 = 103;
const TEST: u16 = 104;
const CONNECT: u16 = 105;
const ENABLE: u16 = 106;
const COPY: u16 = 107;
const SETTINGS: u16 = 108;
const STARTUP: u16 = 110;
const PARTIAL: u16 = 111;
const HOLD: u16 = 112;
const TOGGLE: u16 = 113;
const DEVICE: u16 = 114;
const API: u16 = 115;
const RESOURCE: u16 = 116;
const STATUS: u16 = 117;
const METER: u16 = 118;
const RECOVERY: u16 = 119;
fn resource_id(id: u16) -> PCWSTR {
    PCWSTR(id as usize as *const u16)
}
std::thread_local! {static DISPATCHING:std::cell::Cell<bool>=const{std::cell::Cell::new(false)};}
struct Deferred {
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
    rect: Option<RECT>,
}
std::thread_local! {static DEFERRED:std::cell::RefCell<Vec<Deferred>>=const{std::cell::RefCell::new(Vec::new())};}
unsafe fn flush_deferred() {
    loop {
        let messages = DEFERRED.with(|queue| std::mem::take(&mut *queue.borrow_mut()));
        if messages.is_empty() {
            break;
        }
        for mut item in messages {
            if !unsafe { IsWindow(Some(item.hwnd)) }.as_bool() {
                continue;
            }
            if item.message == WM_PAINT {
                unsafe {
                    let _ = InvalidateRect(Some(item.hwnd), None, false);
                }
                continue;
            }
            if let Some(ref mut rect) = item.rect {
                item.lparam = LPARAM((rect as *mut RECT) as isize);
            }
            unsafe {
                window_proc(item.hwnd, item.message, item.wparam, item.lparam);
            }
        }
    }
}
struct DispatchGuard;
impl Drop for DispatchGuard {
    fn drop(&mut self) {
        DISPATCHING.with(|flag| flag.set(false));
    }
}
mod overlay;
mod settings_window;
mod tray;

struct Draft {
    preferences: crate::storage::Preferences,
    provider: crate::storage::Provider,
    editing: bool,
    dirty: bool,
}
struct App {
    root: HWND,
    settings: HWND,
    overlay: HWND,
    instance: HINSTANCE,
    config: Configuration,
    paths: PortablePaths,
    draft: Draft,
    language: Language,
    devices: Vec<AudioDevice>,
    sink: Option<Sink>,
    session: Option<Session>,
    serial: u64,
    state: State,
    last_text: Option<String>,
    recovery: Option<String>,
    status: String,
    partial: String,
    level: f32,
    hook: Option<Hook>,
    job: Option<JoinHandle<()>>,
    job_cancel: Cancel,
    busy: bool,
    saving: bool,
    pending: Option<Configuration>,
    pending_credentials: bool,
    building: bool,
    font: HFONT,
    overlay_font: HFONT,
    overlay_dpi: i32,
    overlay_lines: Vec<String>,
    wave_bars: [f64; 22],
    wave_position: usize,
    wave_display: f64,
    title_font: HFONT,
    label_font: HFONT,
    icons: [HICON; 3],
    taskbar: u32,
    dpi: i32,
    overlay_error: bool,
    testing_microphone: bool,
    warmup: Option<JoinHandle<()>>,
    warmup_cancel: Arc<platform::Event>,
    clipboard_jobs: Vec<JoinHandle<()>>,
    window_bounds: Option<RECT>,
}
pub fn run() -> AppResult<()> {
    unsafe {
        let _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        let mutex = CreateMutexW(None, false, w!("Local\\SayInput.Desktop"))?;
        if GetLastError() == ERROR_ALREADY_EXISTS {
            let existing = FindWindowW(w!("SayInput.Rust.Root"), w!("SayInput"));
            if let Ok(h) = existing {
                let _ = PostMessageW(Some(h), ACTIVATE, WPARAM(0), LPARAM(0));
            }
            let _ = CloseHandle(mutex);
            return Ok(());
        }
        struct MutexOwner(HANDLE);
        impl Drop for MutexOwner {
            fn drop(&mut self) {
                unsafe {
                    let _ = CloseHandle(self.0);
                }
            }
        }
        let _mutex = MutexOwner(mutex);
        let paths = PortablePaths::from_exe()?;
        let config = Configuration::load(&paths)?;
        platform::startup(config.preferences.start_with_windows)?;
        let instance: HINSTANCE = GetModuleHandleW(None)?.into();
        let _graphics = crate::graphics::Runtime::new()?;
        let _sounds = platform::SoundRuntime::new();
        let icon = LoadIconW(Some(instance), resource_id(1))?;
        let icons = [
            icon,
            LoadIconW(Some(instance), resource_id(2)).unwrap_or(icon),
            LoadIconW(Some(instance), resource_id(3)).unwrap_or(icon),
        ];
        for name in [
            w!("SayInput.Rust.Root"),
            w!("SayInput.Rust.Settings"),
            w!("SayInput.Rust.Overlay"),
        ] {
            let wc = WNDCLASSW {
                lpfnWndProc: Some(window_proc),
                hInstance: instance,
                lpszClassName: name,
                hCursor: LoadCursorW(None, IDC_ARROW)?,
                hIcon: icon,
                hbrBackground: HBRUSH((COLOR_WINDOW.0 + 1) as *mut _),
                ..Default::default()
            };
            if RegisterClassW(&wc) == 0 {
                return Err(AppError::Platform);
            }
        }
        let _ = InitCommonControlsEx(&INITCOMMONCONTROLSEX {
            dwSize: std::mem::size_of::<INITCOMMONCONTROLSEX>() as u32,
            dwICC: ICC_STANDARD_CLASSES | ICC_PROGRESS_CLASS,
        });
        let language = Language::system();
        let draft = Draft {
            preferences: config.preferences.clone(),
            provider: config.provider.clone(),
            editing: !config.provider.has_credentials(),
            dirty: false,
        };
        let mut app = Box::new(App {
            root: HWND::default(),
            settings: HWND::default(),
            overlay: HWND::default(),
            instance,
            config,
            paths,
            draft,
            language,
            devices: Vec::new(),
            sink: None,
            session: None,
            serial: 0,
            state: State::Idle,
            last_text: None,
            recovery: None,
            status: String::new(),
            partial: String::new(),
            level: 0.0,
            hook: None,
            job: None,
            job_cancel: Arc::new(AtomicBool::new(false)),
            busy: false,
            saving: false,
            pending: None,
            pending_credentials: false,
            building: false,
            font: HFONT::default(),
            overlay_font: HFONT::default(),
            overlay_dpi: 96,
            overlay_lines: Vec::new(),
            wave_bars: [0.; 22],
            wave_position: 0,
            wave_display: 0.,
            title_font: HFONT::default(),
            label_font: HFONT::default(),
            icons,
            taskbar: RegisterWindowMessageW(w!("TaskbarCreated")),
            dpi: GetDpiForSystem() as i32,
            overlay_error: false,
            testing_microphone: false,
            warmup: None,
            warmup_cancel: Arc::new(platform::Event::new(true)?),
            clipboard_jobs: Vec::new(),
            window_bounds: None,
        });
        let pointer = app.as_mut() as *mut App;
        app.root = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            w!("SayInput.Rust.Root"),
            w!("SayInput"),
            WS_OVERLAPPED,
            0,
            0,
            0,
            0,
            None,
            None,
            Some(instance),
            Some(pointer.cast()),
        )?;
        app.sink = Some(Sink::new(app.root.0 as usize));
        app.hook = Some(Hook::start(app.root)?);
        app.tray(NIM_ADD);
        if app.config.preferences.enable_audio_warmup {
            let cancel = app.warmup_cancel.clone();
            let open_device = app.config.preferences.enable_device_warmup;
            app.warmup = Some(thread::spawn(move || audio::warmup(open_device, cancel)));
        }
        app.status = if app.config.credentials_need_reentry {
            language
                .t(
                    "无法解密旧凭据，请重新输入。",
                    "Stored credentials could not be decrypted. Please re-enter them.",
                )
                .into()
        } else {
            language
                .t(
                    "请填写凭据并保存，然后到其他应用试用。",
                    "Save your credentials, then use Right Alt in another app.",
                )
                .into()
        };
        if !std::env::args().any(|a| a == "--background") || !app.config.provider.has_credentials()
        {
            DISPATCHING.with(|f| f.set(true));
            app.show_settings();
            DISPATCHING.with(|f| f.set(false));
        }
        let mut message = MSG::default();
        while GetMessageW(&mut message, None, 0, 0).0 > 0 {
            if app.settings.is_invalid() || !IsDialogMessageW(app.settings, &message).as_bool() {
                let _ = TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            flush_deferred();
        }
        app.shutdown();
        let _ = DestroyWindow(app.root);
        Ok(())
    }
}
unsafe extern "system" fn window_proc(
    hwnd: HWND,
    message: u32,
    wparam: WPARAM,
    lparam: LPARAM,
) -> LRESULT {
    unsafe {
        if message == WM_NCCREATE {
            let cs = &*(lparam.0 as *const CREATESTRUCTW);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, cs.lpCreateParams as isize);
            return DefWindowProcW(hwnd, message, wparam, lparam);
        }
        if message == WM_NCDESTROY {
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, 0);
            return DefWindowProcW(hwnd, message, wparam, lparam);
        }
        let ptr = GetWindowLongPtrW(hwnd, GWLP_USERDATA) as *mut App;
        if ptr.is_null() {
            return DefWindowProcW(hwnd, message, wparam, lparam);
        }
        if matches!(
            message,
            WM_CTLCOLORSTATIC | WM_CTLCOLORBTN | WM_CTLCOLOREDIT
        ) {
            let dc = HDC(wparam.0 as *mut _);
            SetBkColor(dc, COLORREF(0xffffff));
            let _ = SetBkMode(dc, TRANSPARENT);
            SetTextColor(
                dc,
                COLORREF(GetSysColor(
                    if IsWindowEnabled(HWND(lparam.0 as *mut _)).as_bool() {
                        COLOR_WINDOWTEXT
                    } else {
                        COLOR_GRAYTEXT
                    },
                )),
            );
            return LRESULT(GetStockObject(WHITE_BRUSH).0 as isize);
        }
        // Native calls may reenter this procedure while an App method is active.
        // Child commands are posted to the root; incidental creation/layout messages
        // never construct a second mutable App reference.
        if message == WM_COMMAND && hwnd != (*ptr).root {
            if !(*ptr).building {
                let _ = PostMessageW(Some((*ptr).root), WM_COMMAND, wparam, lparam);
            }
            return LRESULT(0);
        }
        if message == WM_NOTIFY && lparam.0 != 0 {
            let header = &*(lparam.0 as *const NMHDR);
            if header.idFrom == 130 && header.code == TCN_SELCHANGING {
                return LRESULT(1);
            }
            return DefWindowProcW(hwnd, message, wparam, lparam);
        }
        let overlay_paint = message == WM_PAINT && hwnd == (*ptr).overlay;
        if message == WM_ERASEBKGND && hwnd == (*ptr).overlay {
            return LRESULT(1);
        }
        let handled = matches!(
            message,
            WM_CLOSE
                | WM_COMMAND
                | WM_DRAWITEM
                | WM_NOTIFY
                | ACTIVATE
                | hotkey::KEY
                | session::NOTIFY
                | TRAY
                | WM_TIMER
        ) || message == (*ptr).taskbar
            || overlay_paint
            || (message == WM_DPICHANGED && (hwnd == (*ptr).settings || hwnd == (*ptr).overlay))
            || (message == WM_SIZE
                && hwnd == (*ptr).settings
                && wparam.0 == SIZE_MINIMIZED as usize);
        if !handled {
            return DefWindowProcW(hwnd, message, wparam, lparam);
        }
        if DISPATCHING.with(|f| f.get()) {
            if message == WM_DRAWITEM && lparam.0 != 0 {
                let item = &*(lparam.0 as *const DRAWITEMSTRUCT);
                DEFERRED.with(|queue| {
                    queue.borrow_mut().push(Deferred {
                        hwnd: item.hwndItem,
                        message: WM_PAINT,
                        wparam: WPARAM(0),
                        lparam: LPARAM(0),
                        rect: None,
                    })
                });
                return LRESULT(1);
            }
            let rect = if message == WM_DPICHANGED && lparam.0 != 0 {
                Some(*(lparam.0 as *const RECT))
            } else {
                None
            };
            DEFERRED.with(|queue| {
                queue.borrow_mut().push(Deferred {
                    hwnd,
                    message,
                    wparam,
                    lparam,
                    rect,
                })
            });
            if overlay_paint {
                let mut paint = PAINTSTRUCT::default();
                BeginPaint(hwnd, &mut paint);
                let _ = EndPaint(hwnd, &paint);
            }
            return LRESULT(0);
        }
        DISPATCHING.with(|f| f.set(true));
        let _dispatch = DispatchGuard;
        if overlay_paint {
            (&*ptr).paint_overlay();
            return LRESULT(0);
        }
        let app = &mut *ptr;
        if message == app.taskbar && hwnd == app.root {
            app.tray(NIM_ADD);
            return LRESULT(0);
        }
        match message {
            WM_DRAWITEM if lparam.0 != 0 => {
                let item = &*(lparam.0 as *const DRAWITEMSTRUCT);
                if item.CtlID == 121 {
                    FillRect(
                        item.hDC,
                        &item.rcItem,
                        HBRUSH(GetStockObject(WHITE_BRUSH).0),
                    );
                    crate::graphics::paint_brand(item.hDC, item.rcItem);
                    return LRESULT(1);
                }
                if item.CtlID == 130 {
                    let s = |v| (v * app.dpi + 48) / 96;
                    let white = HBRUSH(GetStockObject(WHITE_BRUSH).0);
                    FillRect(item.hDC, &item.rcItem, white);
                    let border = CreateSolidBrush(COLORREF(0xacacac));
                    let mut panel = item.rcItem;
                    panel.top += s(21);
                    FrameRect(item.hDC, &panel, border);
                    let old = SelectObject(item.hDC, app.font.into());
                    let _ = SetBkMode(item.hDC, TRANSPARENT);
                    let mut left = item.rcItem.left;
                    for (index, label) in [
                        app.t("文字润色", "Text polishing"),
                        app.t("语音识别", "Speech recognition"),
                    ]
                    .into_iter()
                    .enumerate()
                    {
                        let mut text = wide(label);
                        let count = text.len() - 1;
                        let mut size = SIZE::default();
                        let _ = GetTextExtentPoint32W(item.hDC, &text[..count], &mut size);
                        let mut rect = RECT {
                            left,
                            top: item.rcItem.top,
                            right: left + size.cx + s(14),
                            bottom: item.rcItem.top + s(22),
                        };
                        let background = if index == 0 {
                            CreateSolidBrush(COLORREF(0xf0f0f0))
                        } else {
                            white
                        };
                        FillRect(item.hDC, &rect, background);
                        FrameRect(item.hDC, &rect, border);
                        if index == 0 {
                            let _ = DeleteObject(background.into());
                        } else {
                            let gap = RECT {
                                left: rect.left + 1,
                                right: rect.right - 1,
                                top: rect.bottom - 1,
                                bottom: rect.bottom,
                            };
                            FillRect(item.hDC, &gap, white);
                        }
                        SetTextColor(
                            item.hDC,
                            if index == 0 {
                                COLORREF(0x838383)
                            } else {
                                COLORREF(0)
                            },
                        );
                        DrawTextW(
                            item.hDC,
                            &mut text[..count],
                            &mut rect,
                            DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_NOPREFIX,
                        );
                        left = rect.right;
                    }
                    SelectObject(item.hDC, old);
                    let _ = DeleteObject(border.into());
                    return LRESULT(1);
                }
            }
            WM_NOTIFY if lparam.0 != 0 => {
                let header = &*(lparam.0 as *const NMHDR);
                if header.idFrom == 130 && header.code == TCN_SELCHANGING {
                    return LRESULT(1);
                }
            }
            WM_CLOSE => {
                if hwnd == app.settings {
                    app.read_draft();
                    app.destroy_settings();
                } else if hwnd == app.root {
                    app.quit();
                }
                return LRESULT(0);
            }
            WM_COMMAND => {
                if !app.building {
                    app.command(
                        (wparam.0 & 0xffff) as u16,
                        ((wparam.0 >> 16) & 0xffff) as u16,
                    );
                }
                return LRESULT(0);
            }
            ACTIVATE => {
                app.show_settings();
                return LRESULT(0);
            }
            hotkey::KEY => {
                app.trigger(wparam.0);
                return LRESULT(0);
            }
            session::NOTIFY => {
                app.notices();
                return LRESULT(0);
            }
            TRAY => {
                let mouse = (lparam.0 & 0xffff) as u32;
                if mouse == WM_LBUTTONDBLCLK {
                    app.show_settings();
                } else if mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU {
                    app.menu();
                }
                return LRESULT(0);
            }
            WM_SIZE if hwnd == app.settings && wparam.0 == SIZE_MINIMIZED as usize => {
                let _ = PostMessageW(
                    Some(app.root),
                    WM_COMMAND,
                    WPARAM(MINIMIZE as usize),
                    LPARAM(0),
                );
                return LRESULT(0);
            }
            WM_TIMER => {
                if wparam.0 == 1 {
                    app.advance_wave();
                }
                if wparam.0 == 2 {
                    let _ = KillTimer(Some(app.root), 2);
                    app.hide_overlay();
                }
                return LRESULT(0);
            }
            WM_DPICHANGED if hwnd == app.overlay => {
                app.overlay_dpi = (wparam.0 & 0xffff) as i32;
                let _ = DeleteObject(app.overlay_font.into());
                app.overlay_font =
                    CreateFontIndirectW(&crate::graphics::message_font(app.overlay_dpi, 17, 400));
                app.show_overlay();
                return LRESULT(0);
            }
            WM_DPICHANGED if hwnd == app.settings => {
                let bounds = if lparam.0 != 0 {
                    Some(*(lparam.0 as *const RECT))
                } else {
                    None
                };
                app.read_draft();
                app.destroy_settings();
                app.dpi = (wparam.0 & 0xffff) as i32;
                app.window_bounds = bounds;
                app.show_settings();
                return LRESULT(0);
            }
            _ => {}
        }
        DefWindowProcW(hwnd, message, wparam, lparam)
    }
}
impl App {
    fn t<'a>(&self, zh: &'a str, en: &'a str) -> &'a str {
        self.language.t(zh, en)
    }

    unsafe fn command(&mut self, id: u16, notification: u16) {
        match id {
            EXIT => self.quit(),
            SETTINGS => self.show_settings(),
            MINIMIZE => {
                self.read_draft();
                self.destroy_settings();
            }
            COPY => {
                if let Some(text) = self.last_text.clone() {
                    let sink = self.sink.as_ref().unwrap().clone();
                    self.clipboard_jobs.push(thread::spawn(move || {
                        let result = input::copy_text(&text);
                        sink.post(Notice::CopyDone(result));
                    }));
                }
            }
            ENABLE => {
                if self.busy || self.state != State::Idle {
                    return;
                }
                self.draft.preferences.enabled = !self.config.preferences.enabled;
                self.draft.dirty = true;
                self.save(false);
            }
            SAVE => self.save(self.draft.editing),
            EDIT => {
                if self.busy || self.state != State::Idle {
                    return;
                }
                if self.draft.editing {
                    self.save(true);
                } else {
                    self.draft.editing = true;
                    self.refresh_controls();
                }
            }
            TEST => self.test_microphone(),
            CONNECT => self.test_connection(),
            STARTUP | PARTIAL | HOLD | TOGGLE | DEVICE
                if notification == BN_CLICKED as u16 || notification == CBN_SELCHANGE as u16 =>
            {
                self.draft.dirty = true;
                self.refresh_controls();
            }
            API | RESOURCE if notification == EN_CHANGE as u16 => {
                self.draft.dirty = true;
            }
            _ => {}
        }
    }
    unsafe fn save(&mut self, credentials_changed: bool) {
        if self.busy || self.state != State::Idle {
            return;
        }
        self.read_draft();
        let config = Configuration {
            preferences: self.draft.preferences.clone(),
            provider: self.draft.provider.clone(),
            credentials_need_reentry: !credentials_changed && self.config.credentials_need_reentry,
        };
        if credentials_changed && let Err(e) = config.provider.validate() {
            self.show_error(&e);
            return;
        }
        let pending = Configuration {
            preferences: config.preferences.clone(),
            provider: config.provider.clone(),
            credentials_need_reentry: config.credentials_need_reentry,
        };
        let paths = self.paths.clone();
        let sink = self.sink.as_ref().unwrap().clone();
        self.busy = true;
        self.saving = true;
        self.pending = Some(pending);
        self.pending_credentials = credentials_changed;
        self.refresh_controls();
        self.job = Some(thread::spawn(move || {
            let result = config
                .save(&paths, credentials_changed)
                .and_then(|_| platform::startup(config.preferences.start_with_windows));
            sink.post(Notice::Saved(result));
        }));
    }
    unsafe fn test_microphone(&mut self) {
        if self.busy {
            if !self.saving {
                self.job_cancel.store(true, Ordering::Release);
            }
            return;
        }
        if self.state != State::Idle {
            return;
        }
        self.read_draft();
        self.stop_warmup();
        self.busy = true;
        self.testing_microphone = true;
        self.job_cancel = Arc::new(AtomicBool::new(false));
        let cancel = self.job_cancel.clone();
        let sink = self.sink.as_ref().unwrap().clone();
        let device = self.draft.preferences.microphone_device_id.clone();
        self.set_text(TEST, self.t("结束", "Stop"));
        self.refresh_controls();
        self.job = Some(thread::spawn(move || {
            let events = sink.clone();
            let result = (|| -> AppResult<()> {
                let capture = Capture::start(
                    device,
                    None,
                    Arc::new(move |v| events.post(Notice::Level(0, v))),
                )?;
                let started = Instant::now();
                while started.elapsed() < Duration::from_secs(10)
                    && !cancel.load(Ordering::Acquire)
                    && !capture.finished()
                {
                    thread::sleep(Duration::from_millis(40));
                }
                capture.stop()
            })();
            sink.post(Notice::TestDone(result));
        }));
    }
    unsafe fn test_connection(&mut self) {
        if self.busy || self.state != State::Idle {
            return;
        }
        self.read_draft();
        self.stop_warmup();
        let provider = self.draft.provider.clone();
        if let Err(e) = provider.validate() {
            self.show_error(&e);
            return;
        }
        self.busy = true;
        self.job_cancel = Arc::new(AtomicBool::new(false));
        let cancel = self.job_cancel.clone();
        let sink = self.sink.as_ref().unwrap().clone();
        self.status = self.t("正在测试连接…", "Testing connection…").into();
        self.set_text(STATUS, &self.status);
        self.refresh_controls();
        self.job = Some(thread::spawn(move || {
            sink.post(Notice::TestDone(session::connection_test(provider, cancel)));
        }));
    }
    unsafe fn trigger(&mut self, edge: usize) {
        if self.busy || !self.config.preferences.enabled {
            return;
        }
        if edge == 3 {
            if matches!(self.state, State::Starting | State::Recording)
                && let Some(s) = &self.session
            {
                s.cancel();
            }
            return;
        }
        if edge == 2 && self.config.preferences.recording_trigger_mode == 0 {
            return;
        }
        if matches!(self.state, State::Starting | State::Recording) {
            if let Some(s) = &self.session {
                s.stop();
            }
            return;
        }
        if self.state != State::Idle || edge != 1 {
            return;
        }
        if let Err(e) = self.config.provider.validate() {
            self.show_error(&e);
            return;
        }
        let Some(target) = Target::capture() else {
            self.status = self
                .t(
                    "请先点击其他应用的输入位置。",
                    "First select a text field in another app.",
                )
                .into();
            self.balloon(&self.status);
            return;
        };
        self.stop_warmup();
        self.serial += 1;
        self.partial.clear();
        self.level = 0.0;
        self.overlay_error = false;
        let _ = KillTimer(Some(self.root), 2);
        self.state = State::Starting;
        hotkey::CANCEL_ENABLED.store(true, Ordering::Release);
        self.tray(NIM_MODIFY);
        self.show_overlay();
        self.refresh_controls();
        self.session = Some(Session::start(
            self.serial,
            target,
            self.config.provider.clone(),
            self.config.preferences.microphone_device_id.clone(),
            self.paths.clone(),
            self.sink.as_ref().unwrap().clone(),
        ));
    }
    unsafe fn notices(&mut self) {
        let notices = self.sink.as_ref().unwrap().drain();
        for notice in notices {
            match notice {
                Notice::Level(id, value) => {
                    if id == 0 {
                        if self.busy {
                            send(
                                self.control(METER),
                                PBM_SETPOS,
                                WPARAM((value * 1000.0) as usize),
                                LPARAM(0),
                            );
                        }
                    } else if id == self.serial && self.state != State::Idle {
                        self.level = value;
                    }
                }
                Notice::Partial(id, text) if id == self.serial && self.state != State::Idle => {
                    self.partial = text;
                    self.show_overlay();
                }
                Notice::State(id, state) if id == self.serial => {
                    self.state = state;
                    hotkey::CANCEL_ENABLED.store(
                        matches!(state, State::Starting | State::Recording),
                        Ordering::Release,
                    );
                    self.tray(NIM_MODIFY);
                    self.repaint();
                }
                Notice::LastText(id, text) if id == self.serial => {
                    self.last_text = Some(text);
                }
                Notice::Done(id, result) if id == self.serial => {
                    if let Some(s) = self.session.take() {
                        s.join();
                    }
                    self.state = State::Idle;
                    hotkey::CANCEL_ENABLED.store(false, Ordering::Release);
                    self.tray(NIM_MODIFY);
                    match result {
                        Ok(()) => {
                            self.recovery = None;
                            self.status.clear();
                            self.hide_overlay();
                        }
                        Err(e) => {
                            self.recovery = self.last_text.clone();
                            self.show_error(&e);
                        }
                    }
                    self.set_text(STATUS, &self.status);
                    self.set_text(RECOVERY, self.recovery.as_deref().unwrap_or(""));
                    if !self.settings.is_invalid() {
                        let _ = ShowWindow(
                            self.control(RECOVERY),
                            if self.recovery.is_some() {
                                SW_SHOW
                            } else {
                                SW_HIDE
                            },
                        );
                    }
                    self.refresh_controls();
                }
                Notice::TestDone(result) => {
                    if let Some(t) = self.job.take() {
                        let _ = t.join();
                    }
                    self.busy = false;
                    self.testing_microphone = false;
                    self.set_text(TEST, self.t("测试", "Test"));
                    send(self.control(METER), PBM_SETPOS, WPARAM(0), LPARAM(0));
                    match result {
                        Ok(()) => {
                            self.status = self.t("测试完成。", "Test completed.").into();
                            self.set_text(STATUS, &self.status);
                        }
                        Err(AppError::Cancelled) => {}
                        Err(e) => self.show_error(&e),
                    }
                    self.refresh_controls();
                }
                Notice::CopyDone(result) => {
                    if let Err(error) = result {
                        self.balloon(&self.language.error(&error));
                    }
                    let mut remaining = Vec::new();
                    for job in self.clipboard_jobs.drain(..) {
                        if job.is_finished() {
                            let _ = job.join();
                        } else {
                            remaining.push(job);
                        }
                    }
                    self.clipboard_jobs = remaining;
                }
                Notice::Saved(result) => {
                    if let Some(t) = self.job.take() {
                        let _ = t.join();
                    }
                    self.busy = false;
                    self.saving = false;
                    match result {
                        Ok(()) => {
                            self.config = self.pending.take().unwrap();
                            if self.pending_credentials {
                                self.draft.editing = false;
                            }
                            self.draft.dirty = false;
                            self.status = self.t("已保存", "Saved").into();
                            self.set_text(STATUS, &self.status);
                            self.tray(NIM_MODIFY);
                        }
                        Err(e) => {
                            self.pending = None;
                            self.show_error(&e);
                        }
                    }
                    self.refresh_controls();
                }
                _ => {}
            }
        }
    }
    unsafe fn show_error(&mut self, error: &AppError) {
        self.status = self.language.error(error);
        self.set_text(STATUS, &self.status);
        self.balloon(&self.status);
        if self.state == State::Idle {
            self.partial = self.status.clone();
            self.overlay_error = true;
            self.show_overlay();
            SetTimer(Some(self.root), 2, 4000, None);
        }
    }

    unsafe fn quit(&mut self) {
        self.shutdown();
        PostQuitMessage(0);
    }
    unsafe fn shutdown(&mut self) {
        self.stop_warmup();
        hotkey::CANCEL_ENABLED.store(false, Ordering::Release);
        self.hook = None;
        self.job_cancel.store(true, Ordering::Release);
        if let Some(s) = self.session.take() {
            s.cancel();
            s.join();
        }
        if let Some(t) = self.job.take() {
            let _ = t.join();
        }
        for job in self.clipboard_jobs.drain(..) {
            let _ = job.join();
        }
        self.tray(NIM_DELETE);
        self.hide_overlay();
        self.destroy_settings();
        self.last_text = None;
    }
    fn stop_warmup(&mut self) {
        self.warmup_cancel.set();
        if let Some(thread) = self.warmup.take() {
            let _ = thread.join();
        }
    }
}
unsafe fn send(h: HWND, m: u32, w: WPARAM, l: LPARAM) -> LRESULT {
    unsafe { windows::Win32::UI::WindowsAndMessaging::SendMessageW(h, m, Some(w), Some(l)) }
}
