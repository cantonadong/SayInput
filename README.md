# SayInput

Windows 10/11 x64 原生语音输入工具，使用 Rust 与 Windows 系统 API，无需安装 .NET。图标和提示音内嵌，程序入口为 [artifacts/SayInput.exe](artifacts/SayInput.exe)。

下载 [v1.5.0 Windows x64](https://github.com/cantonadong/SayInput/releases/tag/v1.5.0)。ZIP 仅包含 `SayInput.exe` 和 SHA-256 校验文件；解压后运行，升级时保留原有 `data` 与 `recordings` 目录。

## 使用

1. 运行 `artifacts/SayInput.exe`，填写火山引擎流式识别 2.0 的 API Key，Resource ID 默认 `volc.seedasr.sauc.duration`，保存。
2. 选择有输入信号的麦克风，可先做本地音量测试和连接测试。
3. 点击其他应用的输入位置，用右 Alt 录音：点按模式再次按下结束；按住模式松开结束；Esc 取消。
4. 完成后将终稿输入原窗口。无法输入时，可从托盘复制最后文本。

支持实时三行文字、录音音量波形、托盘状态、开机启动和录音期间的系统输出静音恢复。设置窗口关闭或最小化后收进托盘；退出请使用设置或托盘的“退出”。

界面语言启动时跟随 Windows 系统界面语言：中文系统使用简体中文，其他系统使用英文。设置页没有语言选择项，旧配置中的 Language 不再决定界面语言。

## 便携数据

```text
artifacts/
├─ SayInput.exe
├─ data/
│  ├─ settings.json
│  └─ credentials/
└─ recordings/
```

API Key 使用当前 Windows 用户的 DPAPI 加密，移动到其他用户或电脑后需要重新输入。录音滚动保留最近二十条 WAV；转写文字仅保留在当前进程内存中。升级时覆盖同一个 EXE，保留数据目录。

公开分发仅需 EXE，不要打包自己的 data、recordings 或凭据文件。SimpleWall 新放行 EXE 后，需完全退出所有 SayInput 进程再重新启动。

当前 Rust 版本仍需真实桌面与语音验收。已编译和静态检查不代表已完成运行验收；具体步骤见 [人工验收](docs/rust-manual-acceptance.md)。历史 C# 发行版仍可从 [GitHub Releases](https://github.com/cantonadong/SayInput/releases) 获取。

## 构建

需要 Windows GNU Rust 工具链及 LLVM-MinGW/LLD。脚本默认复用本机已有工具目录，可用 `-ToolchainPath` 指定替代目录。

```powershell
# 格式和静态检查，不运行测试
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-rust.ps1 -Lint

# 编译后停止旧进程，覆盖 artifacts/SayInput.exe，不启动程序
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-rust.ps1
```

`-BuildOnly` 仅生成 Rust 构建产物，`-CheckOnly` 仅做编译检查。清理构建缓存后首次编译会重新生成 `rust/target`。

## 结构

- `rust/src`：设置、托盘、悬浮窗、热键、采集、识别协议、输入及兼容存储。
- `icon`、`resources/sounds`：内嵌构建素材。
- `scripts/build-rust.ps1`：构建与同路径部署；`generate-icon.ps1`：图标生成。
- `docs/development-progress.md`：当前交接、验证证据和待办。
- `docs/volcengine-notes.md`：识别协议参考。

未获用户明确要求时，不运行测试或自动启动应用。开发直接使用 main，提交时按文件选择源码和文档，避免纳入便携数据。
