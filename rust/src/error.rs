#[derive(Debug, Clone)]
pub enum AppError {
    Storage,
    Credentials,
    Audio,
    Network,
    Protocol,
    Timeout,
    Cancelled,
    Input,
    Platform,
    Service(u32),
}
pub type AppResult<T> = Result<T, AppError>;
impl From<std::io::Error> for AppError {
    fn from(_: std::io::Error) -> Self {
        Self::Storage
    }
}
impl From<windows::core::Error> for AppError {
    fn from(_: windows::core::Error) -> Self {
        Self::Platform
    }
}
