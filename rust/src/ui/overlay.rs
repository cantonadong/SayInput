use super::*;
use crate::graphics::Surface;
use windows::Win32::Graphics::GdiPlus::{FlushIntentionSync, GdipFlush, RectF};

// Values match RecordingOverlay.xaml: WPF device-independent units, not pixels.
const WIDTH: i32 = 480;
const LINE: i32 = 24;
const TEXT_X: i32 = 23;
const TEXT_Y: i32 = 57;
impl App {
    pub(super) unsafe fn show_overlay(&mut self) {
        if self.overlay.is_invalid() {
            self.overlay = CreateWindowExW(
                WS_EX_LAYERED
                    | WS_EX_TOPMOST
                    | WS_EX_NOACTIVATE
                    | WS_EX_TRANSPARENT
                    | WS_EX_TOOLWINDOW,
                w!("SayInput.Rust.Overlay"),
                w!("SayInput"),
                WS_POPUP,
                0,
                0,
                1,
                1,
                None,
                None,
                Some(self.instance),
                Some((self as *mut Self).cast()),
            )
            .unwrap_or_default();
            self.overlay_dpi = GetDpiForWindow(self.overlay).max(96) as i32;
            self.overlay_font =
                CreateFontIndirectW(&crate::graphics::message_font(self.overlay_dpi, 17, 400));
            self.wave_bars = [0.; 22];
            self.wave_display = 0.;
            self.wave_position = 0;
            SetTimer(Some(self.root), 1, 34, None);
        }
        let text = if self.overlay_error
            || (self.config.preferences.show_partial && !self.partial.is_empty())
        {
            self.partial.clone()
        } else {
            self.t("请说话…", "Speak now…").to_owned()
        };
        self.overlay_lines = self.wrap_overlay(&text);
        self.render_overlay();
    }
    unsafe fn wrap_overlay(&self, text: &str) -> Vec<String> {
        let dc = GetDC(Some(self.overlay));
        let old = SelectObject(dc, self.overlay_font.into());
        let max = (WIDTH - 2 * TEXT_X) * self.overlay_dpi / 96;
        let mut lines = std::collections::VecDeque::new();
        for paragraph in text.split('\n') {
            let mut rest = paragraph.trim_end_matches('\r');
            if rest.is_empty() {
                lines.push_back(String::new());
            }
            while !rest.is_empty() {
                let encoded: Vec<u16> = rest.encode_utf16().collect();
                let (mut fit, mut size) = (0, SIZE::default());
                let _ = GetTextExtentExPointW(
                    dc,
                    PCWSTR(encoded.as_ptr()),
                    encoded.len() as i32,
                    max,
                    Some(&mut fit),
                    None,
                    &mut size,
                );
                let units = fit.max(1) as usize;
                let mut consumed = 0;
                let mut byte_end = 0;
                for (byte, ch) in rest.char_indices() {
                    if consumed + ch.len_utf16() > units && byte_end != 0 {
                        break;
                    }
                    consumed += ch.len_utf16();
                    byte_end = byte + ch.len_utf8();
                }
                if byte_end < rest.len()
                    && let Some((at, _)) = rest[..byte_end]
                        .char_indices()
                        .rev()
                        .find(|(at, c)| *at > byte_end / 2 && c.is_whitespace())
                {
                    byte_end = at + rest[at..].chars().next().unwrap().len_utf8();
                }
                lines.push_back(rest[..byte_end].trim_end().to_owned());
                while lines.len() > 3 {
                    lines.pop_front();
                }
                rest = &rest[byte_end..];
            }
        }
        SelectObject(dc, old);
        ReleaseDC(Some(self.overlay), dc);
        if lines.is_empty() {
            lines.push_back(String::new());
        }
        lines
            .into_iter()
            .rev()
            .take(3)
            .collect::<Vec<_>>()
            .into_iter()
            .rev()
            .collect()
    }
    pub(super) unsafe fn advance_wave(&mut self) {
        if self.overlay.is_invalid() || self.overlay_error {
            return;
        }
        let incoming = self.level as f64;
        let target = (incoming * 3.).clamp(0., 1.);
        self.wave_display += (target - self.wave_display)
            * if incoming > self.wave_display {
                0.65
            } else {
                0.22
            };
        self.wave_bars[self.wave_position % 22] = self.wave_display;
        self.wave_position = (self.wave_position + 1) % 22;
        self.level *= 0.8;
        self.render_overlay();
    }
    pub(super) unsafe fn repaint(&self) {
        if !self.overlay.is_invalid() {
            self.render_overlay();
        }
    }
    pub(super) unsafe fn paint_overlay(&self) {
        let mut paint = PAINTSTRUCT::default();
        BeginPaint(self.overlay, &mut paint);
        let _ = EndPaint(self.overlay, &paint);
        self.render_overlay();
    }
    unsafe fn render_overlay(&self) {
        if self.overlay.is_invalid() {
            return;
        }
        let scale = self.overlay_dpi as f32 / 96.;
        // Border padding 22,16 + 1px border, waveform 28, gap 12, 1–3 lines.
        let height = (74 + LINE * self.overlay_lines.len().clamp(1, 3) as i32).clamp(92, 164);
        let width = (WIDTH as f32 * scale).round() as i32;
        let pixels = (height as f32 * scale).round() as i32;
        let std::result::Result::Ok(surface) = Surface::new(width, pixels, scale) else {
            return;
        };
        surface.rounded(
            RectF {
                X: 0.5,
                Y: 0.5,
                Width: WIDTH as f32 - 1.,
                Height: height as f32 - 1.,
            },
            18.,
            0xF21B2433,
            Some(0xFF40546A),
        );
        for index in 0..22 {
            let h = 3. + self.wave_bars[(self.wave_position + index) % 22] as f32 * 25.;
            let x = (WIDTH as f32 - 132.) / 2. + index as f32 * 6.;
            surface.rounded(
                RectF {
                    X: x,
                    Y: 17. + (28. - h) / 2.,
                    Width: 3.,
                    Height: h,
                },
                1.5,
                0xFF5EDECA,
                None,
            );
        }
        let font = crate::graphics::message_font(96, 17, 400);
        for (row, line) in self.overlay_lines.iter().enumerate() {
            surface.line(
                line,
                TEXT_X as f32,
                (TEXT_Y + row as i32 * LINE) as f32,
                (WIDTH - 2 * TEXT_X) as f32,
                PCWSTR(font.lfFaceName.as_ptr()),
            );
        }
        GdipFlush(surface.graphics, FlushIntentionSync);
        let mut work = RECT::default();
        let _ = SystemParametersInfoW(
            SPI_GETWORKAREA,
            0,
            Some((&mut work as *mut RECT).cast()),
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS(0),
        );
        let position = POINT {
            x: work.left + (work.right - work.left - width) / 2,
            y: work.bottom - pixels - (40. * scale).round() as i32,
        };
        let size = SIZE {
            cx: width,
            cy: pixels,
        };
        let origin = POINT::default();
        let blend = BLENDFUNCTION {
            BlendOp: AC_SRC_OVER as u8,
            BlendFlags: 0,
            SourceConstantAlpha: 255,
            AlphaFormat: AC_SRC_ALPHA as u8,
        };
        let _ = UpdateLayeredWindow(
            self.overlay,
            None,
            Some(&position),
            Some(&size),
            Some(surface.dc),
            Some(&origin),
            COLORREF(0),
            Some(&blend),
            ULW_ALPHA,
        );
        let _ = SetWindowPos(
            self.overlay,
            Some(HWND_TOPMOST),
            position.x,
            position.y,
            width,
            pixels,
            SWP_NOACTIVATE | SWP_SHOWWINDOW,
        );
    }
    pub(super) unsafe fn hide_overlay(&mut self) {
        let _ = KillTimer(Some(self.root), 1);
        if !self.overlay.is_invalid() {
            let _ = DestroyWindow(self.overlay);
            self.overlay = HWND::default();
        }
        if !self.overlay_font.is_invalid() {
            let _ = DeleteObject(self.overlay_font.into());
            self.overlay_font = HFONT::default();
        }
        self.overlay_lines.clear();
        self.partial.clear();
        self.overlay_error = false;
    }
}
