use crate::error::AppResult;
use std::{
    sync::{
        atomic::{AtomicBool, AtomicUsize, Ordering},
        mpsc,
    },
    thread::{self, JoinHandle},
};
use windows::Win32::{
    Foundation::{HWND, LPARAM, LRESULT, WPARAM},
    System::{LibraryLoader::GetModuleHandleW, Threading::GetCurrentThreadId},
    UI::{Input::KeyboardAndMouse::*, WindowsAndMessaging::*},
};
pub const KEY: u32 = 0x8000 + 20;
static WINDOW: AtomicUsize = AtomicUsize::new(0);
pub static CANCEL_ENABLED: AtomicBool = AtomicBool::new(false);
static ALT: AtomicBool = AtomicBool::new(false);
static ESC: AtomicBool = AtomicBool::new(false);
pub struct Hook {
    thread: Option<JoinHandle<()>>,
    id: u32,
}
impl Hook {
    pub fn start(hwnd: HWND) -> AppResult<Self> {
        WINDOW.store(hwnd.0 as usize, Ordering::Release);
        let (tx, rx) = mpsc::sync_channel(1);
        let thread = thread::spawn(move || unsafe {
            let mut msg = MSG::default();
            let _ = PeekMessageW(&mut msg, None, 0, 0, PM_NOREMOVE);
            let result = GetModuleHandleW(None).and_then(|module| {
                SetWindowsHookExW(WH_KEYBOARD_LL, Some(callback), Some(module.into()), 0)
            });
            match result {
                Ok(hook) => {
                    let _ = tx.send(Ok(GetCurrentThreadId()));
                    while GetMessageW(&mut msg, None, 0, 0).0 > 0 {}
                    let _ = UnhookWindowsHookEx(hook);
                }
                Err(e) => {
                    let _ = tx.send(Err(e));
                }
            }
        });
        match rx.recv() {
            Ok(Ok(id)) => Ok(Self {
                thread: Some(thread),
                id,
            }),
            _ => {
                let _ = thread.join();
                Err(crate::error::AppError::Platform)
            }
        }
    }
}
impl Drop for Hook {
    fn drop(&mut self) {
        unsafe {
            let _ = PostThreadMessageW(self.id, WM_QUIT, WPARAM(0), LPARAM(0));
        }
        if let Some(t) = self.thread.take() {
            let _ = t.join();
        }
        WINDOW.store(0, Ordering::Release);
    }
}
unsafe extern "system" fn callback(code: i32, w: WPARAM, l: LPARAM) -> LRESULT {
    unsafe {
        if code == 0 {
            let data = &*(l.0 as *const KBDLLHOOKSTRUCT);
            let message = w.0 as u32;
            if data.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED) == KBDLLHOOKSTRUCT_FLAGS(0) {
                let down = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
                let up = message == WM_KEYUP || message == WM_SYSKEYUP;
                let mut edge = 0;
                if data.vkCode == VK_RMENU.0 as u32 {
                    if down && !ALT.swap(true, Ordering::AcqRel) {
                        edge = 1;
                    } else if up && ALT.swap(false, Ordering::AcqRel) {
                        edge = 2;
                    }
                }
                let mut suppress = data.vkCode == VK_RMENU.0 as u32;
                if data.vkCode == VK_ESCAPE.0 as u32 {
                    if down && CANCEL_ENABLED.load(Ordering::Acquire) {
                        if !ESC.swap(true, Ordering::AcqRel) {
                            edge = 3;
                        }
                        suppress = true;
                    }
                    if up && ESC.swap(false, Ordering::AcqRel) {
                        suppress = true;
                    }
                }
                if edge != 0 {
                    let h = WINDOW.load(Ordering::Acquire);
                    if h != 0 {
                        let _ = PostMessageW(Some(HWND(h as *mut _)), KEY, WPARAM(edge), LPARAM(0));
                    }
                }
                if suppress {
                    return LRESULT(1);
                }
            }
        }
        CallNextHookEx(None, code, w, l)
    }
}
