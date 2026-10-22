#![windows_subsystem = "windows"]

mod audio;
mod error;
mod graphics;
mod hotkey;
mod i18n;
mod input;
mod platform;
mod session;
mod speech;
mod spool;
mod storage;
mod transport;
mod ui;

fn main() {
    if let Err(error) = ui::run() {
        let language = i18n::Language::system();
        unsafe {
            windows::Win32::UI::WindowsAndMessaging::MessageBoxW(
                None,
                windows::core::PCWSTR(platform::wide(&language.error(&error)).as_ptr()),
                windows::core::w!("SayInput"),
                windows::Win32::UI::WindowsAndMessaging::MB_OK
                    | windows::Win32::UI::WindowsAndMessaging::MB_ICONERROR,
            );
        }
    }
}
