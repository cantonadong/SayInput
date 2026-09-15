<p align="center">
  <img src="icon/icon.png" alt="SayInput" width="96">
</p>

<h1 align="center">SayInput</h1>

<p align="center">轻量、便携的 Windows 流式语音输入工具</p>

<p align="center">
  <a href="https://github.com/cantonadong/SayInput/releases/latest">下载最新版</a>
  ·
  <a href="docs/compatibility.md">兼容性</a>
  ·
  <a href="docs/performance.md">性能记录</a>
</p>

## 功能

SayInput 将麦克风语音实时发送到火山引擎“流式识别 2.0”，识别完成后把文字输入到当前应用。

- 使用全局右 Alt 键录音，可选择“按住说话”或“点击开始、再次点击结束”
- 录音时显示居中波形和最多三行实时识别文字
- 支持 `Esc` 取消当前录音，并立即释放麦克风
- 录音期间静音其他系统输出，结束或取消后自动恢复
- 托盘常驻，并用不同图标表示录音和转写状态
- 可选开机启动、实时文字和麦克风设备
- 本地滚动保留最近 20 条 WAV 录音
- API Key 使用 Windows DPAPI 加密
- 自包含 Windows x64 便携包，无需安装 .NET

## 下载

当前正式版本为 **v1.1.0**。

从 [GitHub Releases](https://github.com/cantonadong/SayInput/releases/latest) 下载 `SayInput-1.1.0-win-x64.zip`，解压完整目录后运行 `SayInput.exe`。请勿只复制 EXE，运行所需文件都在发行目录内。

SHA-256：

```text
307ede90c518b55fcbe3b06ec6205eff5af1ffe14b7bfa276eccbaa56a061b45
```

支持 Windows 10/11 x64。

## 配置

1. 在火山引擎开通“流式识别 2.0”，取得 API Key。
2. 启动 SayInput，在“语音识别”页填写：
   - 服务商：火山引擎
   - API Key：火山引擎控制台提供的密钥
   - Resource ID：默认 `volc.seedasr.sauc.duration`
3. 选择麦克风，可点击“测试”查看输入音量，再点击“测试连接”。
4. 点击底部“保存”。

如果 SimpleWall 刚放行 SayInput，请完全退出程序并重新启动，规则才会对新进程生效。

## 使用

先在目标应用中点击需要输入文字的位置，然后使用右 Alt：

- **按住模式**：按住右 Alt 开始录音，松开后停止并转写。
- **点击模式**：按一下右 Alt 开始录音，再按一下停止并转写。
- **取消录音**：录音过程中按 `Esc`，本次录音与识别会被取消。

识别结束后，SayInput 会把最终文字一次性输入到录音开始时选中的窗口。关闭设置窗口只会将程序收进系统托盘；需要彻底关闭时，请点击设置页“退出”或使用托盘菜单。

没有录到有效声音时不会弹出提示，可直接再次录音。

## 便携数据

SayInput 的配置、凭据和录音都保存在 EXE 同级目录：

```text
SayInput/
├─ SayInput.exe
├─ data/
│  ├─ settings.json
│  └─ credentials/
├─ recordings/
└─ resources/
```

- API Key 通过当前 Windows 用户的 DPAPI 加密，复制到其他用户或电脑后不能直接解密。
- `recordings` 只保留最近 20 条 WAV。
- SayInput 不在本地保存转写文字。
- 升级时可将新版本解压覆盖到原目录，以继续使用现有配置。

## 常见问题

**麦克风测试没有音量，但其他软件可以录音**

在 SayInput 的麦克风下拉框中选择正确的输入设备。Windows 默认输入设备可能与其他软件自行选择的设备不同。

**测试连接失败或一直没有识别结果**

确认 API Key、Resource ID 和账号开通的服务匹配。流式识别 2.0 小时版默认使用 `volc.seedasr.sauc.duration`。检查防火墙是否允许 `SayInput.exe` 访问网络；SimpleWall 放行后需要重启程序。

**已勾选开机启动，但任务管理器中没有启动项**

点击底部“保存”，然后重新启动一次 SayInput。程序启动和保存时都会校准当前用户的启动项，并清理旧版 `VoiceTyper` 启动项。

**为什么内存占用比某些同类工具高**

发行包是自包含的 .NET 8 WPF 应用，运行时和界面框架会占用一部分常驻内存。程序空闲时不会持续高频轮询；实际性能记录见 [docs/performance.md](docs/performance.md)。

## 隐私与网络

录音会发送到火山引擎进行语音识别，因此使用行为同时受火山引擎账号配置和服务条款约束。本地只保存滚动录音，不保存转写文字。正式发行流程会扫描发布目录与 ZIP，阻止 `data`、`recordings` 和 `key.txt` 被打包。

仓库中的测试凭据全部为虚构值；请勿提交真实 API Key。

## 开发

需要 Windows x64 和 .NET 8 SDK。

```powershell
dotnet restore
dotnet build VoiceTyper.sln -c Release
dotnet test VoiceTyper.sln -c Release --filter "Category!=Hardware"
```

生成自包含便携包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
```

默认输出到 `artifacts/SayInput-1.1.0-win-x64`。项目内部仍保留 `VoiceTyper.*` 工程与命名空间，用户可见产品名和进程名均为 SayInput。

## 项目结构

- `VoiceTyper.Core`：会话状态、音频管线和平台无关接口
- `VoiceTyper.Windows`：麦克风、热键、窗口捕获、文字注入和 Windows 安全存储
- `VoiceTyper.Volcengine`：火山引擎流式识别协议
- `VoiceTyper.App`：WPF 设置页、悬浮窗、托盘与运行时组装
- `tests`：Core、Windows 和火山引擎协议测试

开发规范与历史记录见 [DEVELOPMENT.md](DEVELOPMENT.md) 和 [开发进展](docs/development-progress.md)。
