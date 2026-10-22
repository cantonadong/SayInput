use super::*;
impl App {
    pub(super) unsafe fn tray(&self, action: NOTIFY_ICON_MESSAGE) {
        let index = match self.state {
            State::Starting | State::Recording => 1,
            State::Finalizing | State::Injecting => 2,
            State::Idle => 0,
        };
        let mut data = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: self.root,
            uID: 1,
            uFlags: NIF_ICON | NIF_MESSAGE | NIF_TIP,
            uCallbackMessage: TRAY,
            hIcon: self.icons[index],
            ..Default::default()
        };
        let tip = match index {
            1 => self.t("SayInput · 正在录音", "SayInput · Recording"),
            2 => self.t("SayInput · 正在转写", "SayInput · Transcribing"),
            _ => self.t("SayInput · 右 Alt 录音", "SayInput · Right Alt recording"),
        };
        for (a, b) in data.szTip.iter_mut().zip(tip.encode_utf16()) {
            *a = b;
        }
        let _ = Shell_NotifyIconW(action, &data);
    }
    pub(super) unsafe fn balloon(&self, text: &str) {
        let mut data = NOTIFYICONDATAW {
            cbSize: std::mem::size_of::<NOTIFYICONDATAW>() as u32,
            hWnd: self.root,
            uID: 1,
            uFlags: NIF_INFO,
            dwInfoFlags: NIIF_INFO,
            ..Default::default()
        };
        for (a, b) in data.szInfo.iter_mut().take(255).zip(text.encode_utf16()) {
            *a = b;
        }
        for (a, b) in data.szInfoTitle.iter_mut().zip("SayInput".encode_utf16()) {
            *a = b;
        }
        let _ = Shell_NotifyIconW(NIM_MODIFY, &data);
    }
    pub(super) unsafe fn menu(&mut self) {
        let Ok(menu) = CreatePopupMenu() else {
            return;
        };
        let flag = if self.config.preferences.enabled {
            MF_CHECKED
        } else {
            MF_UNCHECKED
        };
        let _ = AppendMenuW(
            menu,
            MF_STRING | flag,
            ENABLE as usize,
            PCWSTR(wide(self.t("启用右 Alt 录音", "Enable Right Alt recording")).as_ptr()),
        );
        let _ = AppendMenuW(
            menu,
            MF_STRING,
            SETTINGS as usize,
            PCWSTR(wide(self.t("设置", "Settings")).as_ptr()),
        );
        let _ = AppendMenuW(
            menu,
            MF_STRING
                | if self.last_text.is_some() {
                    MF_ENABLED
                } else {
                    MF_GRAYED
                },
            COPY as usize,
            PCWSTR(wide(self.t("复制最后文本", "Copy last text")).as_ptr()),
        );
        let _ = AppendMenuW(menu, MF_SEPARATOR, 0, PCWSTR::null());
        let _ = AppendMenuW(
            menu,
            MF_STRING,
            EXIT as usize,
            PCWSTR(wide(self.t("退出", "Exit")).as_ptr()),
        );
        let mut point = POINT::default();
        let _ = GetCursorPos(&mut point);
        let _ = SetForegroundWindow(self.root);
        let command = TrackPopupMenu(
            menu,
            TPM_RETURNCMD | TPM_RIGHTBUTTON,
            point.x,
            point.y,
            Some(0),
            self.root,
            None,
        );
        let _ = DestroyMenu(menu);
        if command.0 != 0 {
            self.command(command.0 as u16, 0);
        }
        let _ = PostMessageW(Some(self.root), WM_NULL, WPARAM(0), LPARAM(0));
    }
}
