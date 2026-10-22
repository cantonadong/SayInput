use crate::error::AppError;
#[derive(Clone, Copy)]
pub struct Language(pub bool);
impl Language {
    pub fn system() -> Self {
        Self(unsafe { windows::Win32::Globalization::GetUserDefaultUILanguage() & 0x3ff == 4 })
    }
    pub fn t<'a>(&self, zh: &'a str, en: &'a str) -> &'a str {
        if self.0 { zh } else { en }
    }
    pub fn error(&self, e: &AppError) -> String {
        match e{
        AppError::Credentials=>self.t("请填写有效的 API Key 和 Resource ID。","Enter valid credentials and Resource ID.").into(),
        AppError::Storage=>self.t("无法保存或读取文件，请检查磁盘空间和目录权限。","Cannot access files. Check disk space and folder permissions.").into(),
        AppError::Audio=>self.t("麦克风采集失败，请检查所选设备。","Microphone capture failed. Check the selected device.").into(),
        AppError::Network=>self.t("网络不可用或识别服务连接失败，请检查防火墙。","Network or speech connection failed. Check your firewall.").into(),
        AppError::Protocol=>self.t("识别服务返回了无效或不完整的结果。","The speech service returned an invalid or incomplete result.").into(),
        AppError::Timeout=>self.t("上传或识别超时，请重试。","Upload or recognition timed out. Please retry.").into(),
        AppError::Cancelled=>self.t("已取消。","Cancelled.").into(),
        AppError::Input=>self.t("无法安全完成输入，请核对原窗口并从托盘复制最后文本。","Cannot safely finish typing. Check the target and copy the last text from the tray.").into(),
        AppError::Platform=>self.t("Windows 操作失败，请重试。","A Windows operation failed. Please retry.").into(),
        AppError::Service(code)=>format!("{} ({code})",self.t("识别服务拒绝请求，请检查凭据、资源权限或服务状态。","Speech request rejected. Check credentials, resource access or service status.")),
    }
    }
}
