use super::*;
impl App {
    pub(super) unsafe fn control(&self, id: u16) -> HWND {
        GetDlgItem(Some(self.settings), id as i32).unwrap_or_default()
    }
    pub(super) unsafe fn set_text(&self, id: u16, text: &str) {
        let _ = SetWindowTextW(self.control(id), PCWSTR(wide(text).as_ptr()));
    }
    pub(super) unsafe fn get_text(&self, id: u16) -> String {
        let h = self.control(id);
        let mut buffer = vec![0u16; GetWindowTextLengthW(h) as usize + 1];
        let n = GetWindowTextW(h, &mut buffer);
        String::from_utf16_lossy(&buffer[..n as usize])
    }
    pub(super) unsafe fn checked(&self, id: u16) -> bool {
        send(self.control(id), BM_GETCHECK, WPARAM(0), LPARAM(0)).0 == BST_CHECKED.0 as isize
    }
    pub(super) unsafe fn check(&self, id: u16, value: bool) {
        send(
            self.control(id),
            BM_SETCHECK,
            WPARAM(if value {
                BST_CHECKED.0
            } else {
                BST_UNCHECKED.0
            } as usize),
            LPARAM(0),
        );
    }
    #[expect(
        clippy::too_many_arguments,
        reason = "Control factory mirrors Win32 geometry and styles"
    )]
    pub(super) unsafe fn add(
        &self,
        class: PCWSTR,
        text: &str,
        id: u16,
        style: WINDOW_STYLE,
        x: i32,
        y: i32,
        width: i32,
        height: i32,
    ) -> HWND {
        let scale = |v| (v * self.dpi + 48) / 96;
        let h = CreateWindowExW(
            WINDOW_EX_STYLE(0),
            class,
            PCWSTR(wide(text).as_ptr()),
            WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | style,
            scale(x),
            scale(y),
            scale(width),
            scale(height),
            Some(self.settings),
            Some(HMENU(id as usize as *mut _)),
            Some(self.instance),
            None,
        )
        .unwrap_or_default();
        send(h, WM_SETFONT, WPARAM(self.font.0 as usize), LPARAM(1));
        h
    }
    pub(super) unsafe fn show_settings(&mut self) {
        if !self.settings.is_invalid() {
            let _ = ShowWindow(self.settings, SW_RESTORE);
            let _ = SetForegroundWindow(self.settings);
            return;
        }
        self.building = true;
        self.devices = audio::devices().unwrap_or_default();
        let s = |v: i32| v * self.dpi / 96;
        let width = s(430);
        let height = s(700);
        let mut work = RECT::default();
        let _ = SystemParametersInfoW(
            SPI_GETWORKAREA,
            0,
            Some((&mut work as *mut RECT).cast()),
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS(0),
        );
        let x = self
            .window_bounds
            .map_or(work.left + (work.right - work.left - width) / 2, |r| r.left);
        let y = self
            .window_bounds
            .map_or(work.top + (work.bottom - work.top - height) / 2, |r| r.top);
        self.settings = CreateWindowExW(
            WS_EX_APPWINDOW,
            w!("SayInput.Rust.Settings"),
            PCWSTR(wide(self.t("SayInput · 设置", "SayInput · Settings")).as_ptr()),
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX,
            x,
            y,
            width,
            height,
            None,
            None,
            Some(self.instance),
            Some((self as *mut Self).cast()),
        )
        .unwrap_or_default();
        let dpi = GetDpiForWindow(self.settings).max(96);
        self.dpi = dpi as i32;
        let s = |v: i32| v * dpi as i32 / 96;
        let _ = SetWindowPos(
            self.settings,
            None,
            x,
            y,
            s(430),
            s(700),
            SWP_NOZORDER | SWP_NOACTIVATE,
        );
        self.font = CreateFontIndirectW(&crate::graphics::message_font(self.dpi, 12, 400));
        self.title_font = CreateFontIndirectW(&crate::graphics::message_font(self.dpi, 16, 600));
        self.label_font = CreateFontIndirectW(&crate::graphics::message_font(self.dpi, 12, 600));
        let mut client = RECT::default();
        let _ = GetClientRect(self.settings, &mut client);
        let cw = client.right * 96 / dpi as i32;
        let ch = client.bottom * 96 / dpi as i32;
        let content = cw - 48;
        let footer = ch - 20 - 32;
        let tabs_bottom = footer - 20;
        // Mirrors MainWindow.xaml: 24/18 outer margin, 42px brand, 18px gaps,
        // 64px label column and three equal footer buttons.
        let title = self.t("SayInput 设置", "SayInput Settings");
        let measure = |text: &str, font: HFONT| {
            let dc = GetDC(Some(self.settings));
            let old = SelectObject(dc, font.into());
            let mut size = SIZE::default();
            let _ =
                GetTextExtentPoint32W(dc, &wide(text)[..text.encode_utf16().count()], &mut size);
            SelectObject(dc, old);
            ReleaseDC(Some(self.settings), dc);
            (size.cx * 96 + self.dpi - 1) / self.dpi
        };
        let title_width = measure(title, self.title_font);
        let startup_width = measure(self.t("开机启动", "Start with Windows"), self.font) + 17;
        let hold_width = measure(self.t("按住", "Hold"), self.font) + 17;
        let brand_start = (cw - (42 + 12 + title_width)) / 2;
        self.add(
            w!("STATIC"),
            "",
            121,
            WINDOW_STYLE(windows::Win32::System::SystemServices::SS_OWNERDRAW.0),
            brand_start,
            18,
            42,
            42,
        );
        let header = self.add(
            w!("STATIC"),
            title,
            122,
            WINDOW_STYLE(windows::Win32::System::SystemServices::SS_CENTERIMAGE.0),
            brand_start + 54,
            18,
            title_width,
            42,
        );
        send(
            header,
            WM_SETFONT,
            WPARAM(self.title_font.0 as usize),
            LPARAM(0),
        );
        for (caption, y) in [
            (self.t("设置项", "Options"), 76),
            (self.t("使用方式", "Mode"), 110),
            (self.t("麦克风", "Microphone"), 152),
        ] {
            let label = self.add(
                w!("STATIC"),
                caption,
                0,
                WINDOW_STYLE(windows::Win32::System::SystemServices::SS_CENTERIMAGE.0),
                24,
                y,
                64,
                20,
            );
            send(
                label,
                WM_SETFONT,
                WPARAM(self.label_font.0 as usize),
                LPARAM(0),
            );
        }
        self.add(
            w!("BUTTON"),
            self.t("开机启动", "Start with Windows"),
            STARTUP,
            WINDOW_STYLE(BS_AUTOCHECKBOX as u32) | WS_TABSTOP,
            88,
            76,
            startup_width,
            20,
        );
        self.add(
            w!("BUTTON"),
            self.t("显示实时文字", "Live transcript"),
            PARTIAL,
            WINDOW_STYLE(BS_AUTOCHECKBOX as u32) | WS_TABSTOP,
            88 + startup_width + 26,
            76,
            content - 160,
            20,
        );
        self.add(
            w!("BUTTON"),
            self.t("按住", "Hold"),
            HOLD,
            WINDOW_STYLE(BS_AUTORADIOBUTTON as u32) | WS_GROUP | WS_TABSTOP,
            88,
            110,
            hold_width,
            20,
        );
        self.add(
            w!("BUTTON"),
            self.t("点击", "Toggle"),
            TOGGLE,
            WINDOW_STYLE(BS_AUTORADIOBUTTON as u32) | WS_TABSTOP,
            88 + hold_width + 46,
            110,
            70,
            20,
        );
        let device_width = content - 64 - 96 - 8;
        let devices = self.add(
            w!("COMBOBOX"),
            "",
            DEVICE,
            WINDOW_STYLE(CBS_DROPDOWNLIST as u32) | WS_TABSTOP | WS_VSCROLL,
            88,
            146,
            device_width,
            240,
        );
        send(
            devices,
            CB_SETITEMHEIGHT,
            WPARAM(usize::MAX),
            LPARAM(s(26) as isize),
        );
        send(
            devices,
            CB_ADDSTRING,
            WPARAM(0),
            LPARAM(wide(self.t("跟随系统", "System default")).as_ptr() as isize),
        );
        for device in &self.devices {
            send(
                devices,
                CB_ADDSTRING,
                WPARAM(0),
                LPARAM(wide(&device.name).as_ptr() as isize),
            );
        }
        let index = self
            .devices
            .iter()
            .position(|d| Some(&d.id) == self.draft.preferences.microphone_device_id.as_ref())
            .map_or(0, |i| i + 1);
        send(devices, CB_SETCURSEL, WPARAM(index), LPARAM(0));
        self.add(
            w!("BUTTON"),
            self.t("测试", "Test"),
            TEST,
            WS_TABSTOP,
            cw - 24 - 96,
            146,
            96,
            32,
        );
        let meter = self.add(
            PROGRESS_CLASSW,
            "",
            METER,
            WINDOW_STYLE(0),
            88,
            186,
            content - 64,
            14,
        );
        send(meter, PBM_SETRANGE32, WPARAM(0), LPARAM(1000));
        send(meter, PBM_SETBARCOLOR, WPARAM(0), LPARAM(0x003bd820));
        self.add(
            w!("STATIC"),
            "",
            130,
            WINDOW_STYLE(windows::Win32::System::SystemServices::SS_OWNERDRAW.0),
            24,
            218,
            content,
            tabs_bottom - 218,
        );
        let inner_x = 40;
        let inner_width = content - 32;
        let mut row = 253;
        let provider_label = self.add(
            w!("STATIC"),
            self.t("服务商", "Provider"),
            0,
            WINDOW_STYLE(0),
            inner_x,
            row,
            inner_width,
            16,
        );
        send(
            provider_label,
            WM_SETFONT,
            WPARAM(self.label_font.0 as usize),
            LPARAM(0),
        );
        row += 22;
        let provider = self.add(
            w!("COMBOBOX"),
            "",
            120,
            WINDOW_STYLE(CBS_DROPDOWNLIST as u32),
            inner_x,
            row,
            inner_width,
            80,
        );
        send(
            provider,
            CB_SETITEMHEIGHT,
            WPARAM(usize::MAX),
            LPARAM(s(26) as isize),
        );
        send(
            provider,
            CB_ADDSTRING,
            WPARAM(0),
            LPARAM(wide(self.t("火山引擎", "Volcengine")).as_ptr() as isize),
        );
        send(provider, CB_SETCURSEL, WPARAM(0), LPARAM(0));
        let _ = EnableWindow(provider, false);
        row += 46;
        self.add(
            w!("STATIC"),
            "API Key",
            0,
            WINDOW_STYLE(0),
            inner_x,
            row,
            inner_width,
            16,
        );
        row += 22;
        self.add(
            w!("EDIT"),
            &self.draft.provider.api_key,
            API,
            WS_BORDER | WS_TABSTOP | WINDOW_STYLE((ES_PASSWORD | ES_AUTOHSCROLL) as u32),
            inner_x,
            row,
            inner_width,
            32,
        );
        row += 46;
        self.add(
            w!("STATIC"),
            "Resource ID",
            0,
            WINDOW_STYLE(0),
            inner_x,
            row,
            inner_width,
            16,
        );
        row += 22;
        self.add(
            w!("EDIT"),
            &self.draft.provider.resource_id,
            RESOURCE,
            WS_BORDER | WS_TABSTOP | WINDOW_STYLE(ES_AUTOHSCROLL as u32),
            inner_x,
            row,
            inner_width,
            32,
        );
        row += 46;
        let button_group = (cw - 182) / 2;
        self.add(
            w!("BUTTON"),
            "",
            EDIT,
            WS_TABSTOP,
            button_group,
            row,
            82,
            32,
        );
        self.add(
            w!("BUTTON"),
            self.t("测试连接", "Test connection"),
            CONNECT,
            WS_TABSTOP,
            button_group + 90,
            row,
            92,
            32,
        );
        row += 42;
        self.add(
            w!("STATIC"),
            &self.status,
            STATUS,
            WINDOW_STYLE(0),
            inner_x,
            row,
            inner_width,
            44,
        );
        let recovery = self.add(
            w!("EDIT"),
            self.recovery.as_deref().unwrap_or(""),
            RECOVERY,
            WINDOW_STYLE((ES_READONLY | ES_MULTILINE | ES_AUTOVSCROLL) as u32) | WS_VSCROLL,
            inner_x,
            row + 44,
            inner_width,
            (tabs_bottom - row - 60).clamp(0, 55),
        );
        if self.recovery.is_none() {
            let _ = ShowWindow(recovery, SW_HIDE);
        }
        let group = (cw - 296) / 2;
        for (id, zh, en, x) in [
            (SAVE, "保存", "Save", group),
            (MINIMIZE, "最小化", "Minimize", group + 102),
            (EXIT, "退出", "Exit", group + 204),
        ] {
            self.add(
                w!("BUTTON"),
                self.t(zh, en),
                id,
                WS_TABSTOP,
                x,
                footer,
                92,
                32,
            );
        }
        self.check(STARTUP, self.draft.preferences.start_with_windows);
        self.check(PARTIAL, self.draft.preferences.show_partial);
        self.check(HOLD, self.draft.preferences.recording_trigger_mode == 1);
        self.check(TOGGLE, self.draft.preferences.recording_trigger_mode == 0);
        // The tab background must stay behind its sibling form controls.
        let _ = SetWindowPos(
            self.control(130),
            Some(HWND_BOTTOM),
            0,
            0,
            0,
            0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE,
        );
        self.building = false;
        self.refresh_controls();
        let _ = ShowWindow(self.settings, SW_SHOW);
        let _ = SetForegroundWindow(self.settings);
    }

    pub(super) unsafe fn read_draft(&mut self) {
        if self.settings.is_invalid() {
            return;
        }
        self.draft.preferences.start_with_windows = self.checked(STARTUP);
        self.draft.preferences.show_partial = self.checked(PARTIAL);
        self.draft.preferences.recording_trigger_mode = u32::from(self.checked(HOLD));
        let index = send(self.control(DEVICE), CB_GETCURSEL, WPARAM(0), LPARAM(0)).0;
        if index >= 0 {
            self.draft.preferences.microphone_device_id = if index == 0 {
                None
            } else {
                self.devices.get(index as usize - 1).map(|d| d.id.clone())
            };
        }
        if self.draft.editing {
            self.draft.provider.api_key = self.get_text(API);
            self.draft.provider.resource_id = self.get_text(RESOURCE);
        }
    }
    pub(super) unsafe fn destroy_settings(&mut self) {
        if !self.settings.is_invalid() {
            let mut bounds = RECT::default();
            if GetWindowRect(self.settings, &mut bounds).is_ok() {
                self.window_bounds = Some(bounds);
            }
            let old = self.settings;
            self.settings = HWND::default();
            let _ = DestroyWindow(old);
        }
        if !self.font.is_invalid() {
            let _ = DeleteObject(self.font.into());
            self.font = HFONT::default();
            for font in [&mut self.title_font, &mut self.label_font] {
                if !font.is_invalid() {
                    let _ = DeleteObject((*font).into());
                    *font = HFONT::default();
                }
            }
        }
    }
    pub(super) unsafe fn refresh_controls(&self) {
        if self.settings.is_invalid() {
            return;
        }
        let idle = self.state == State::Idle && !self.busy;
        for id in [STARTUP, PARTIAL, HOLD, TOGGLE, DEVICE, EDIT, CONNECT] {
            let _ = EnableWindow(self.control(id), idle);
        }
        for id in [API, RESOURCE] {
            let _ = EnableWindow(self.control(id), idle && self.draft.editing);
        }
        let _ = EnableWindow(
            self.control(TEST),
            self.state == State::Idle && (!self.busy || self.testing_microphone),
        );
        let _ = EnableWindow(
            self.control(SAVE),
            idle && (self.draft.dirty || self.draft.editing),
        );
        self.set_text(
            EDIT,
            self.t(
                if self.draft.editing {
                    "保存"
                } else {
                    "编辑"
                },
                if self.draft.editing { "Save" } else { "Edit" },
            ),
        );
    }
}
