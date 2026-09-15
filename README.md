# SayInput

Windows 10/11 x64 全局语音输入工具，支持点按切换和按住说话两种录音方式，使用 .NET 8、C# 12 和 WPF。当前正式版本为 **1.1.0**。

已支持麦克风采集、火山引擎流式识别、实时转写预览、系统托盘状态动画、提示音和 final 文本上屏。下方 Task 1–4 为早期开发记录。

## 使用方法

运行 `artifacts/SayInput-1.1.0-win-x64/SayInput.exe`，保留整个目录；Windows x64 自带运行时包，无需另装 .NET。

1. 在设置中填入火山语音服务的 API Key；Resource ID 必须对应账号已开通的资源。
2. 点击麦克风旁的“测试”检查音量，再“测试连接”，最后点击底部“保存”。
3. 打开记事本并点击输入位置，按所选方式使用右 Alt：按住说话并松开结束，或按一下开始、再按一下结束。
4. 关闭设置窗口后在托盘待命；从托盘重新打开设置或退出程序。

如果 SimpleWall 刚放行联网，请完全退出 SayInput 后重新启动，再测试连接。语音发送到火山引擎进行识别；本地 `recordings` 目录滚动保留最近20条 WAV，不保存转写文本。凭据由当前 Windows 用户的 DPAPI 加密，并保存在 EXE 同级 `data/credentials`。

优先反馈：能否连接、音量条是否变化、开头是否缺字、停止到上屏是否延迟、是否重复输入。详细验收与测量见 [兼容性清单](docs/compatibility.md) 和 [性能记录](docs/performance.md)。

## 规格

`DEVELOPMENT.md` 是从用户提供的 `spec.md` 原样复制的开发规格。后续以 DEVELOPMENT.md 为权威，spec.md 保留为原始输入。每次只执行一个 Task，完成并验证后停止。

## 项目边界

- `VoiceTyper.Core`：平台无关接口、record、状态枚举；无外部包。
- `VoiceTyper.Windows`：Windows 平台适配，现已实现 JSON 设置、原子文件写入和 DPAPI 凭据存储。
- `VoiceTyper.Volcengine`：Provider 适配项目，目前仅引用 Core。
- `VoiceTyper.App`：WPF 入口，引用三个项目；后续承担依赖注入和 UI。
- `tests/`：三个 xUnit 项目；Windows 项目现有 30 个设置/凭据/热键测试，Core 和 Volcengine 暂无测试用例。

NAudio、CommunityToolkit.Mvvm、Microsoft.Extensions.DependencyInjection、Logging、Options 在相应实现任务引入，避免骨架阶段增加未使用依赖。

## 构建

安装 Windows x64 .NET 8 SDK 后，在根目录运行：

```powershell
dotnet restore
dotnet build VoiceTyper.sln -c Debug
dotnet test VoiceTyper.sln -c Debug
```

本机开发 SDK 安装于 `D:\Program\dotnet`。构建脚本不修改系统 PATH：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1 -Action restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1 -Action build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/publish.ps1
```

## 契约约定

- 第 19 节指定的音频和识别接口保持原签名。
- AudioChunk 是 16 kHz、16-bit、单声道小端 PCM；生产者必须保证内存在消费完成前有效，不能在异步消费者仍使用时复用缓冲。
- RecognitionUpdated 带 SessionId，用于识别过期回调；partial 只能显示，CompleteAsync 返回 final。
- DictationSession 保存最初目标窗口，不保存识别器或设备资源。
- Core 不定义 Provider 鉴权格式。ICredentialStore 用于 OS 保护存储，AppSettings 不包含 Secret。Provider 的非敏感配置在 Task 2 按官方接口需求补充。
- 性能时间差使用单调时钟，Snapshot 返回后不可继续变动。
- 其余接口是依据模块职责补充的最小契约；状态转换、音频和输入行为尚未实现。

## 后续规格问题

1. 750 ms 覆盖式 ring buffer 不能独自保证 800 ms 建连期间的首部音频不丢失。Task 6/11 前需要明确 bounded queue 容量和溢出处理，不能静默覆盖首部。
2. Idle 关闭麦克风时，本地缓冲不能恢复设备开始采集之前的声音；需要实际测量启动延迟，不能声称绝对零丢字。
3. 状态图没有定义 Starting 时 Right Alt UP 的处理；Task 16 前需确定如何停止并完成短会话。

以上问题不影响 Task 1 的声明和项目边界，本任务不擅自修改对应行为。

## Task 1 验证结果（2026-09-14）

- SDK：本机临时目录 .NET SDK 8.0.425，Windows x64。
- `dotnet restore`：成功，7 个项目。首次遇到沙箱用户配置目录访问和 NuGet 网络问题；使用进程级临时 APPDATA、CLI_HOME、NUGET_PACKAGES 并允许下载依赖后成功。
- `dotnet build VoiceTyper.sln -c Debug`：成功，0 warning、0 error。
- `dotnet test VoiceTyper.sln -c Debug`：退出码 0，三个测试程序集均报告没有测试用例。Task 1 仅接口和工程骨架，此结果不代表任何业务功能已测试。
- 独立代码审查：现有 Core Contract 和依赖边界无问题；审查时待加入的三个测试项目现已全部加入解决方案。
- 规格副本 SHA256 与原始 spec.md 一致。
- 尚无语音输入功能可供人工测试；Task 2 未开始。

Git origin 已设置为用户指定的 GitHub 仓库。没有 fetch 或 push；只有用户明确要求时才允许上传。`key.txt` 已加入 .gitignore，本任务未读取密钥。Windows 所有权检查需要时仅使用命令级 `-c safe.directory=C:/Me/Dev/SayInput`，未改变全局 Git 配置。

## Task 2：设置与安全凭据

- `src/VoiceTyper.Windows/Settings/JsonSettingsStore.cs`：默认保存到 `%LOCALAPPDATA%/VoiceTyper/settings.json`。不存在、损坏或 null 配置使用默认值，加载不覆盖原文件；权限和 IO 错误正常上抛。
- `src/VoiceTyper.Windows/Security/DpapiCredentialStore.cs`：使用 DPAPI CurrentUser，凭据单独加密保存到 `%LOCALAPPDATA%/VoiceTyper/credentials/`。密钥名称的 SHA256 用于安全文件名及额外 entropy；不输出凭据，明文字节在转换后清零。
- `src/VoiceTyper.Windows/Storage/AtomicFile.cs`：同目录临时文件写入、flush 到磁盘，再替换目标文件。串行处理低频保存，取消/替换失败保留旧文件，尽力清理临时文件。
- 测试使用临时目录和虚构 secret，覆盖默认值、坏 JSON、部分配置、读写往返、并发保存、失败保留旧文件、取消、真实 DPAPI 解密、损坏密文、路径隔离及删除。
- 新增依赖：Microsoft `System.Security.Cryptography.ProtectedData` 8.0.0。

验证（2026-09-14）：restore 成功；Debug build 0 warning、0 error；整套 test 成功，Windows 20 passed、0 failed、0 skipped，另两个测试程序集暂无用例。真实 DPAPI 测试需要加载当前 Windows 用户配置的本机进程，受限沙箱无法访问保护密钥。本次最终测试在本机用户上下文完成。

参考：[Microsoft DPAPI 文档](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata?view=net-8.0)。

当前没有语音输入测试版；按规格 Task 2 完成后停止，下一项为 Task 3（Right Alt 全局 Hook）。未读取 key.txt，未执行 push。

## Task 3：Right Alt Hook 与本机热键测试版

启动 `artifacts/hotkey-test/VoiceTyper.App.exe`。这是 Windows x64 self-contained Release 包，无需另装 .NET；请保留同目录全部依赖文件。此版本仅测试热键，不能语音输入。

1. 点击“开始测试”。
2. 切到记事本，按住右 Alt 约 5 秒，再松开。
3. 返回测试窗口，确认“按下 1 次 · 松开 1 次”，时长接近 5 秒。
4. 重复一次应各变为 2；左 Alt 和其他普通按键不能增加计数。
5. 点击“停止测试”后再按右 Alt，计数不变；关闭窗口退出。

启用时右 Alt 专用于测试并被消费；其他键、软件注入键放行，不记录用户其他输入。事件订阅者必须快速返回，只能异步调度 UI/网络等工作，不能在 Hook 回调内调用 Start/Stop/Dispose。独立线程使用阻塞 GetMessage，没有键盘轮询或高频 Timer。

新增 `src/VoiceTyper.Windows/Keyboard/` 三个实现文件、供测试访问 internal 类型的 AssemblyInfo，以及 `tests/VoiceTyper.Windows.Tests/Keyboard/` 两个测试文件。App MainWindow 改为显式启用/停止的诊断窗口。

验证（2026-09-14）：Debug build 0 warning、0 error；整套测试 Windows 30 passed、0 failed，另外两个程序集暂无用例。8 个按键逻辑测试、原生 Hook 十次启停/重启及 Dispose 测试通过。Release self-contained publish 成功；实际 exe 启动、窗口创建、关闭退出码 0。独立代码审查未发现问题。实体键盘“按住 5 秒”尚待用户测试，未声称已完成人工验收。

官方依据：[LowLevelKeyboardProc](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)、[PostThreadMessageW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-postthreadmessagew)。

## Task 4：目标窗口捕获

Right Alt 按下时同步捕获外部前台窗口的 HWND、PID、进程名和标题，再调度诊断 UI；松开时保留原快照。元数据读取后重新校验窗口及 PID，拒绝自身窗口、已关闭窗口、归属变化和无法读取的进程。EnsureForegroundAsync 当前只验证原窗口仍在前台，焦点恢复留待上屏任务。

验证（2026-09-14）：Release build 成功，0 warning、0 error；Windows 测试 41 passed、0 failed、0 skipped，Core/Volcengine 暂无测试用例。受限沙箱中 5 项 DPAPI 测试因用户配置不可用失败，在本机用户上下文重跑整套测试后全部通过。使用既有本地 NuGet 缓存完成 Windows x64 self-contained Release publish。

测试包：`artifacts/window-test/VoiceTyper.App.exe`，请保留同目录依赖。启动并点击“开始测试”，切到记事本按住右 Alt，再切换到另一个窗口后松开；返回诊断窗口，确认目标仍是按下时的记事本。关闭目标后重新按下时应捕获新的前台窗口。在诊断窗口自身按下时应显示无可用外部窗口。

实体键盘及跨应用人工验收尚未完成；此包仅测试热键与窗口捕获，尚不支持语音输入。
