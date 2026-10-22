use crate::transport::Cancel;
use crate::{
    error::{AppError, AppResult},
    platform::wide,
};
use std::{sync::atomic::Ordering, thread, time::Duration};
use windows::Win32::{
    Foundation::{HANDLE, HGLOBAL, HWND},
    System::{DataExchange::*, Memory::*},
    UI::{Input::KeyboardAndMouse::*, WindowsAndMessaging::*},
};
use windows::core::w;
#[derive(Clone, Copy)]
pub struct Target {
    pub hwnd: usize,
    pub pid: u32,
}
impl Target {
    pub fn capture() -> Option<Self> {
        unsafe {
            let hwnd = GetForegroundWindow();
            let mut pid = 0;
            GetWindowThreadProcessId(hwnd, Some(&mut pid));
            if hwnd.is_invalid() || pid == std::process::id() {
                None
            } else {
                Some(Self {
                    hwnd: hwnd.0 as usize,
                    pid,
                })
            }
        }
    }
    fn foreground(&self) -> bool {
        unsafe {
            let hwnd = HWND(self.hwnd as *mut _);
            let mut pid = 0;
            GetWindowThreadProcessId(hwnd, Some(&mut pid));
            if !IsWindow(Some(hwnd)).as_bool() || pid != self.pid {
                return false;
            }
            if GetForegroundWindow() != hwnd {
                let _ = SetForegroundWindow(hwnd);
            }
            GetForegroundWindow() == hwnd
        }
    }
}
fn modifiers() -> bool {
    unsafe {
        [VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN]
            .iter()
            .any(|v| GetAsyncKeyState(v.0 as i32) < 0)
    }
}
fn key(vk: VIRTUAL_KEY, flags: KEYBD_EVENT_FLAGS, scan: u16) -> INPUT {
    INPUT {
        r#type: INPUT_KEYBOARD,
        Anonymous: INPUT_0 {
            ki: KEYBDINPUT {
                wVk: vk,
                wScan: scan,
                dwFlags: flags,
                time: 0,
                dwExtraInfo: 0,
            },
        },
    }
}
fn check_cancel(cancel: &Cancel) -> AppResult<()> {
    if cancel.load(Ordering::Acquire) {
        Err(AppError::Cancelled)
    } else {
        Ok(())
    }
}
pub fn inject(target: Target, text: &str, cancel: &Cancel) -> AppResult<()> {
    check_cancel(cancel)?;
    if text.is_empty() {
        return Ok(());
    }
    for _ in 0..25 {
        check_cancel(cancel)?;
        if !modifiers() {
            break;
        }
        thread::sleep(Duration::from_millis(20));
    }
    check_cancel(cancel)?;
    if target.foreground() && !modifiers() {
        let inputs = text
            .encode_utf16()
            .flat_map(|u| {
                [
                    key(VIRTUAL_KEY(0), KEYEVENTF_UNICODE, u),
                    key(VIRTUAL_KEY(0), KEYEVENTF_UNICODE | KEYEVENTF_KEYUP, u),
                ]
            })
            .collect::<Vec<_>>();
        check_cancel(cancel)?;
        let sent = unsafe { SendInput(&inputs, std::mem::size_of::<INPUT>() as i32) } as usize;
        if sent == inputs.len() {
            return Ok(());
        }
        if sent != 0 {
            return Err(AppError::Input);
        }
    }
    check_cancel(cancel)?;
    let clipboard = Clipboard::open()?;
    let saved = Snapshot::capture_locked(&clipboard)?;
    check_cancel(cancel)?;
    if let Err(error) = write_text_locked(text, &clipboard) {
        let _ = saved.restore_locked(&clipboard);
        return Err(error);
    }
    let version = unsafe { GetClipboardSequenceNumber() };
    drop(clipboard);
    let _restore = Restore {
        saved: Some(saved),
        version,
    };
    check_cancel(cancel)?;
    if !target.foreground() || modifiers() {
        return Err(AppError::Input);
    }
    let batch = [
        key(VK_CONTROL, KEYBD_EVENT_FLAGS(0), 0),
        key(VK_V, KEYBD_EVENT_FLAGS(0), 0),
        key(VK_V, KEYEVENTF_KEYUP, 0),
        key(VK_CONTROL, KEYEVENTF_KEYUP, 0),
    ];
    check_cancel(cancel)?;
    if unsafe { SendInput(&batch, std::mem::size_of::<INPUT>() as i32) } != 4 {
        unsafe {
            SendInput(&batch[2..], std::mem::size_of::<INPUT>() as i32);
        }
        return Err(AppError::Input);
    }
    thread::sleep(Duration::from_millis(350));
    Ok(())
}
struct Clipboard(HWND);
impl Clipboard {
    fn open() -> AppResult<Self> {
        let owner = unsafe {
            CreateWindowExW(
                WINDOW_EX_STYLE(0),
                w!("STATIC"),
                w!(""),
                WINDOW_STYLE(0),
                0,
                0,
                0,
                0,
                Some(HWND_MESSAGE),
                None,
                None,
                None,
            )?
        };
        for i in 0..5 {
            if unsafe { OpenClipboard(Some(owner)) }.is_ok() {
                return Ok(Self(owner));
            }
            if i < 4 {
                thread::sleep(Duration::from_millis(20));
            }
        }
        unsafe {
            let _ = DestroyWindow(owner);
        }
        Err(AppError::Input)
    }
}
impl Drop for Clipboard {
    fn drop(&mut self) {
        unsafe {
            let _ = CloseClipboard();
            let _ = DestroyWindow(self.0);
        }
    }
}
struct Memory(HGLOBAL);
impl Drop for Memory {
    fn drop(&mut self) {
        unsafe {
            let _ = windows::Win32::Foundation::GlobalFree(Some(self.0));
        }
    }
}
fn memory(bytes: &[u8]) -> AppResult<Memory> {
    unsafe {
        let block = Memory(GlobalAlloc(GMEM_MOVEABLE, bytes.len())?);
        let ptr = GlobalLock(block.0);
        if ptr.is_null() {
            return Err(AppError::Input);
        }
        std::ptr::copy_nonoverlapping(bytes.as_ptr(), ptr.cast(), bytes.len());
        let _ = GlobalUnlock(block.0);
        Ok(block)
    }
}
pub fn copy_text(text: &str) -> AppResult<()> {
    let clipboard = Clipboard::open()?;
    write_text_locked(text, &clipboard)
}
fn write_text_locked(text: &str, _clipboard: &Clipboard) -> AppResult<()> {
    let encoded = wide(text);
    let bytes = unsafe { std::slice::from_raw_parts(encoded.as_ptr().cast(), encoded.len() * 2) };
    let block = memory(bytes)?;
    unsafe {
        EmptyClipboard()?;
        SetClipboardData(13, Some(HANDLE(block.0.0)))?;
    }
    std::mem::forget(block);
    Ok(())
}
enum Saved {
    Memory(u32, Memory),
    Bitmap(u32, windows::Win32::Graphics::Gdi::HBITMAP),
    Metafile(u32, windows::Win32::Graphics::Gdi::HENHMETAFILE),
}
impl Drop for Saved {
    fn drop(&mut self) {
        unsafe {
            match self {
                Self::Memory(_, _) => {}
                Self::Bitmap(_, v) => {
                    let _ = windows::Win32::Graphics::Gdi::DeleteObject((*v).into());
                }
                Self::Metafile(_, v) => {
                    let _ = windows::Win32::Graphics::Gdi::DeleteEnhMetaFile(Some(*v));
                }
            }
        }
    }
}
struct Snapshot(Vec<Saved>);
struct Restore {
    saved: Option<Snapshot>,
    version: u32,
}
impl Drop for Restore {
    fn drop(&mut self) {
        if let Some(saved) = self.saved.take() {
            let _ = saved.restore(self.version);
        }
    }
}
impl Snapshot {
    fn capture_locked(_clipboard: &Clipboard) -> AppResult<Self> {
        use windows::Win32::Graphics::Gdi::*;
        unsafe {
            let mut result = Self(Vec::new());
            let mut format = 0;
            loop {
                format = EnumClipboardFormats(format);
                if format == 0 {
                    break;
                }
                let handle = GetClipboardData(format)?;
                if format == 2 {
                    let copy = CopyImage(handle, IMAGE_BITMAP, 0, 0, LR_CREATEDIBSECTION)?;
                    result.0.push(Saved::Bitmap(format, HBITMAP(copy.0)));
                } else if format == 14 {
                    let copy = CopyEnhMetaFileW(HENHMETAFILE(handle.0), None);
                    if copy.is_invalid() {
                        return Err(AppError::Input);
                    };
                    result.0.push(Saved::Metafile(format, copy));
                } else {
                    if [3, 9, 0x80, 0x81, 0x82, 0x83, 0x8e].contains(&format) {
                        return Err(AppError::Input);
                    }
                    let original = HGLOBAL(handle.0);
                    let size = GlobalSize(original);
                    if size == 0 {
                        return Err(AppError::Input);
                    }
                    let ptr = GlobalLock(original);
                    if ptr.is_null() {
                        return Err(AppError::Input);
                    }
                    let copy = memory(std::slice::from_raw_parts(ptr.cast(), size));
                    let _ = GlobalUnlock(original);
                    result.0.push(Saved::Memory(format, copy?));
                }
            }
            Ok(result)
        }
    }
    fn restore(self, version: u32) -> AppResult<()> {
        let _clipboard = Clipboard::open()?;
        unsafe {
            if GetClipboardSequenceNumber() != version {
                return Ok(());
            }
        }
        self.restore_locked(&_clipboard)
    }
    fn restore_locked(mut self, _clipboard: &Clipboard) -> AppResult<()> {
        unsafe {
            EmptyClipboard()?;
            for item in self.0.drain(..) {
                let (format, handle) = match &item {
                    Saved::Memory(f, m) => (*f, HANDLE(m.0.0)),
                    Saved::Bitmap(f, m) => (*f, HANDLE(m.0)),
                    Saved::Metafile(f, m) => (*f, HANDLE(m.0)),
                };
                SetClipboardData(format, Some(handle))?;
                std::mem::forget(item);
            }
        }
        Ok(())
    }
}
