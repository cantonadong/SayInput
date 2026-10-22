#![allow(unsafe_op_in_unsafe_fn)]
use crate::{
    error::{AppError, AppResult},
    platform::wide,
};
use windows::{
    Win32::{
        Foundation::RECT,
        Graphics::{Gdi::*, GdiPlus::*},
        UI::Shell::SHCreateMemStream,
    },
    core::PCWSTR,
};
pub unsafe fn message_font(dpi: i32, size: i32, weight: i32) -> LOGFONTW {
    use windows::Win32::UI::{HiDpi::SystemParametersInfoForDpi, WindowsAndMessaging::*};
    let mut metrics = NONCLIENTMETRICSW {
        cbSize: std::mem::size_of::<NONCLIENTMETRICSW>() as u32,
        ..Default::default()
    };
    let _ = SystemParametersInfoForDpi(
        SPI_GETNONCLIENTMETRICS.0,
        metrics.cbSize,
        Some((&mut metrics as *mut NONCLIENTMETRICSW).cast()),
        0,
        dpi as u32,
    );
    let mut font = metrics.lfMessageFont;
    font.lfHeight = -(size * dpi + 48) / 96;
    font.lfWeight = weight;
    font.lfQuality = CLEARTYPE_QUALITY;
    font
}
pub struct Runtime(usize);
impl Runtime {
    pub fn new() -> AppResult<Self> {
        unsafe {
            let mut token = 0;
            let input = GdiplusStartupInput {
                GdiplusVersion: 1,
                ..Default::default()
            };
            if GdiplusStartup(&mut token, &input, std::ptr::null_mut()) != Ok {
                return Err(AppError::Platform);
            }
            std::result::Result::Ok(Self(token))
        }
    }
}
impl Drop for Runtime {
    fn drop(&mut self) {
        unsafe {
            GdiplusShutdown(self.0);
        }
    }
}
pub struct Surface {
    pub dc: HDC,
    bitmap: HBITMAP,
    old: HGDIOBJ,
    image: *mut GpBitmap,
    pub graphics: *mut GpGraphics,
}
impl Surface {
    pub unsafe fn new(width: i32, height: i32, scale: f32) -> AppResult<Self> {
        let dc = CreateCompatibleDC(None);
        let mut bits = std::ptr::null_mut();
        let info = BITMAPINFO {
            bmiHeader: BITMAPINFOHEADER {
                biSize: std::mem::size_of::<BITMAPINFOHEADER>() as u32,
                biWidth: width,
                biHeight: -height,
                biPlanes: 1,
                biBitCount: 32,
                biCompression: BI_RGB.0,
                ..Default::default()
            },
            ..Default::default()
        };
        let bitmap = match CreateDIBSection(Some(dc), &info, DIB_RGB_COLORS, &mut bits, None, 0) {
            std::result::Result::Ok(v) => v,
            Err(e) => {
                let _ = DeleteDC(dc);
                return Err(e.into());
            }
        };
        let old = SelectObject(dc, bitmap.into());
        std::ptr::write_bytes(bits.cast::<u8>(), 0, (width * height * 4) as usize);
        let mut surface = Self {
            dc,
            bitmap,
            old,
            image: std::ptr::null_mut(),
            graphics: std::ptr::null_mut(),
        };
        if GdipCreateBitmapFromScan0(
            width,
            height,
            width * 4,
            0xE200B,
            Some(bits.cast()),
            &mut surface.image,
        ) != Ok
        {
            return Err(AppError::Platform);
        }
        if GdipGetImageGraphicsContext(surface.image.cast(), &mut surface.graphics) != Ok {
            return Err(AppError::Platform);
        }
        GdipSetSmoothingMode(surface.graphics, SmoothingModeAntiAlias8x8);
        GdipSetPageUnit(surface.graphics, UnitPixel);
        GdipSetTextRenderingHint(surface.graphics, TextRenderingHintAntiAliasGridFit);
        GdipScaleWorldTransform(surface.graphics, scale, scale, MatrixOrderPrepend);
        std::result::Result::Ok(surface)
    }
    pub unsafe fn rounded(&self, rect: RectF, radius: f32, color: u32, border: Option<u32>) {
        let mut path = std::ptr::null_mut();
        if GdipCreatePath(FillModeAlternate, &mut path) != Ok {
            return;
        }
        let d = radius * 2.;
        for (x, y, start) in [
            (rect.X, rect.Y, 180.),
            (rect.X + rect.Width - d, rect.Y, 270.),
            (rect.X + rect.Width - d, rect.Y + rect.Height - d, 0.),
            (rect.X, rect.Y + rect.Height - d, 90.),
        ] {
            GdipAddPathArc(path, x, y, d, d, start, 90.);
        }
        GdipClosePathFigure(path);
        let mut brush = std::ptr::null_mut();
        GdipCreateSolidFill(color, &mut brush);
        if !brush.is_null() {
            GdipFillPath(self.graphics, brush.cast(), path);
            GdipDeleteBrush(brush.cast());
        }
        if let Some(color) = border {
            let mut pen = std::ptr::null_mut();
            GdipCreatePen1(color, 1., UnitPixel, &mut pen);
            if !pen.is_null() {
                GdipDrawPath(self.graphics, pen, path);
                GdipDeletePen(pen);
            }
        }
        GdipDeletePath(path);
    }
    pub unsafe fn line(&self, text: &str, x: f32, y: f32, width: f32, face: PCWSTR) {
        let (mut family, mut font, mut brush, mut format) = (
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            std::ptr::null_mut(),
        );
        GdipCreateFontFamilyFromName(face, std::ptr::null_mut(), &mut family);
        if !family.is_null() {
            GdipCreateFont(family, 17., 0, UnitPixel, &mut font);
        }
        GdipCreateSolidFill(0xffffffff, &mut brush);
        GdipStringFormatGetGenericTypographic(&mut format);
        if !format.is_null() {
            GdipSetStringFormatFlags(format, StringFormatFlagsNoWrap.0);
        }
        if !font.is_null() && !brush.is_null() {
            let encoded = wide(text);
            let rect = RectF {
                X: x,
                Y: y,
                Width: width,
                Height: 24.,
            };
            GdipDrawString(
                self.graphics,
                PCWSTR(encoded.as_ptr()),
                (encoded.len() - 1) as i32,
                font,
                &rect,
                format,
                brush.cast(),
            );
        }
        if !format.is_null() {
            GdipDeleteStringFormat(format);
        }
        if !brush.is_null() {
            GdipDeleteBrush(brush.cast());
        }
        if !font.is_null() {
            GdipDeleteFont(font);
        }
        if !family.is_null() {
            GdipDeleteFontFamily(family);
        }
    }
}
impl Drop for Surface {
    fn drop(&mut self) {
        unsafe {
            if !self.graphics.is_null() {
                GdipDeleteGraphics(self.graphics);
            }
            if !self.image.is_null() {
                GdipDisposeImage(self.image.cast());
            }
            SelectObject(self.dc, self.old);
            let _ = DeleteObject(self.bitmap.into());
            let _ = DeleteDC(self.dc);
        }
    }
}
pub unsafe fn paint_brand(dc: HDC, rect: RECT) {
    let Some(stream) = SHCreateMemStream(Some(include_bytes!("../../icon/icon.png"))) else {
        return;
    };
    let (mut image, mut graphics) = (std::ptr::null_mut(), std::ptr::null_mut());
    GdipCreateBitmapFromStream(&stream, &mut image);
    GdipCreateFromHDC(dc, &mut graphics);
    if !image.is_null() && !graphics.is_null() {
        GdipSetInterpolationMode(graphics, InterpolationModeHighQualityBicubic);
        GdipDrawImageRectI(
            graphics,
            image.cast(),
            rect.left,
            rect.top,
            rect.right - rect.left,
            rect.bottom - rect.top,
        );
    }
    if !graphics.is_null() {
        GdipDeleteGraphics(graphics);
    }
    if !image.is_null() {
        GdipDisposeImage(image.cast());
    }
}
