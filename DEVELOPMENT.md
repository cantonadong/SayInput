# VoiceTyper for Windows — Codex 开发主规格文档

> 版本：1.1  
> 日期：2026-09-14  
> 目标平台：Windows 10 / Windows 11 x64  
> 主要语言：C#  
> UI：WPF  
> 运行时：.NET 8  
> 开发模式：Codex 驱动，按任务逐步开发，适合的部分优先测试驱动

---

# 0. Codex 执行规则

本文件是项目唯一权威规格。

Codex 必须遵守：

1. 修改任何代码前，先完整阅读本文档。
2. 严格按照 **第 20 节实施任务** 的顺序开发。
3. 每次只完成一个任务。
4. 每个任务完成后，先运行规定测试，再进入下一任务。
5. 不得擅自修改架构、公开接口、状态机、Right Alt 行为、ASR 处理逻辑、文本上屏逻辑。
6. 如果本文档要求与 Windows API 或火山引擎当前官方接口冲突，停止该任务的相关实现，说明冲突，并提出最小兼容修改方案。
7. 不增加与 MVP 无关的功能。
8. 文件职责必须清晰，禁止出现巨型 God Class。
9. 平台相关功能必须通过接口隔离，便于单元测试。
10. ASR 密钥不得以明文方式写入配置文件。
11. 默认日志中禁止记录 Access Token、API Key、完整鉴权 Header、原始麦克风 PCM。
12. 在声称任务完成前，必须执行 build 和相关测试。
13. 性能不是后期优化项，而是 MVP 的一级验收标准。
14. “低资源占用、最低冷启动、零丢字、无感上屏”优先级高于额外功能数量。
15. 所有时间和性能指标均以 Release Build 为准，不以 Debug 模式为准。

第一阶段目标不是 Windows 输入法 IME，而是一个 Windows 全局语音输入工具。

---

# 1. 产品定义

## 1.1 产品名称

工作名：

```text
VoiceTyper
```

未来可以改名。

不要让产品名侵入底层协议层或领域接口。

## 1.2 核心价值

VoiceTyper 是一个 Windows 后台语音输入工具。

用户在任意普通 Windows 程序中把光标放到输入位置后：

```text
按住 Right Alt
    ↓
立即开始讲话
    ↓
程序立即获取音频
    ↓
火山引擎流式识别
    ↓
悬浮窗实时显示 partial 结果
    ↓
松开 Right Alt
    ↓
获得 final 文本
    ↓
一次性无感上屏到原输入位置
```

完整流程：

```text
目标程序中已有输入光标
        ↓
Right Alt DOWN
        ↓
立即记录目标前台窗口
        ↓
立即开始麦克风采集
        ↓
立即进入本地预缓冲
        ↓
显示录音悬浮窗
        ↓
建立 / 确认火山 ASR WebSocket
        ↓
将预缓冲音频 + 后续实时音频顺序发送
        ↓
实时接收 partial
        ↓
partial 只显示在悬浮窗
        ↓
Right Alt UP
        ↓
停止采集新音频
        ↓
flush 已采集音频
        ↓
请求 / 等待 final
        ↓
恢复 / 校验原目标窗口
        ↓
final 文本仅上屏一次
        ↓
隐藏悬浮窗
        ↓
回到 Idle
```

## 1.3 固定交互模式

MVP 采用：

```text
C 模式
```

规则：

- partial 识别结果实时显示在悬浮窗。
- partial 不得写入目标输入框。
- 松开 Right Alt 后只使用 final 结果。
- final 只允许上屏一次。

这是 v1 硬性要求。

---

# 2. MVP 范围

## 2.1 必须实现

MVP 必须提供：

- Windows 10/11 x64。
- 后台系统托盘运行。
- Right Alt 全局按住说话。
- Right Alt DOWN 开始录音。
- Right Alt UP 结束录音。
- 默认麦克风和可选麦克风。
- 火山引擎大模型流式语音识别。
- 中文识别。
- partial 实时显示。
- 流畅的轻量波形。
- final 一次性输入原目标程序。
- Unicode SendInput。
- Clipboard + Ctrl+V fallback。
- 设置窗口。
- 火山引擎凭据设置。
- 麦克风选择。
- 开机启动。
- 基础日志和诊断。
- 网络、麦克风、输入失败时可靠恢复。
- 录音结束后不泄漏麦克风、线程、WebSocket。
- 单实例运行。
- 极低 Idle CPU。
- 尽量低内存。
- 最低首次冷启动。
- 最低每次录音启动延迟。
- 用户按键后立即讲话时，不丢最前面的字。

## 2.2 MVP 明确不做

v1 不做：

- Windows TSF/IME。
- macOS。
- Linux。
- 本地离线 ASR。
- AI 润色。
- LLM 改写。
- 翻译。
- 转写历史数据库。
- 云同步。
- 登录账户。
- 长期录音保存。
- WAV 文件保存。
- 自定义热键编辑器。
- Toggle 模式。
- 唤醒词。
- VAD 自动开始/结束。
- 多 ASR Provider。
- 自动更新。
- GPU 加速。
- 每应用独立配置。
- 跟随文本光标悬浮窗。
- 富文本输入。
- 自动回车发送。

---

# 3. 产品成功标准

## 3.1 功能验收

测试流程：

1. 打开 Notepad。
2. 点击输入区域。
3. 按住 Right Alt。
4. 立即开始说中文，不需要等待 UI 出现。
5. 程序必须捕获从开口第一音节开始的音频。
6. 悬浮窗出现。
7. partial 中文逐步显示。
8. 松开 Right Alt。
9. 录音立即停止。
10. 收到 final。
11. final 文本只输入一次。
12. 悬浮窗关闭。
13. 程序保持后台待命。

基线程序：

- Windows Notepad
- Chrome
- Edge
- VS Code
- Word
- 至少一个 Electron 应用

## 3.2 稳定性验收

连续进行：

```text
100 次短语音输入
```

要求：

- 不重复上屏。
- 不丢前几个字。
- 不出现麦克风一直占用。
- 不出现悬浮窗卡死。
- 不出现 Hook 未释放。
- 不崩溃。
- 一次 ASR 失败不影响下一次。
- 内存不得持续增长。
- 每次完成后 CPU 必须回到 Idle 基线。

---

# 4. 一级性能目标

性能为 MVP 一级要求。

VoiceTyper 在 Idle 时应“像不存在一样”。

---

## 4.1 Idle

Release 模式下，程序启动稳定后目标：

```text
平均 CPU：           <= 0.2%
周期性 CPU 峰值：    不允许由轮询造成
Working Set 目标：   <= 70 MB
Private Memory：     尽量低
麦克风：             不占用
ASR WebSocket：      默认断开
高频 Timer：         0
后台轮询：           0
波形渲染：           0
```

允许 .NET/WPF Runtime 带来一定固定内存开销。

最终必须实际测量，并写入：

```text
docs/performance.md
```

---

## 4.2 录音 + 流式识别

普通录音时目标：

```text
平均 CPU：                <= 3%
Working Set 目标：        <= 100 MB
音频 Callback：           禁止阻塞网络 IO
音频队列：                bounded
波形最大刷新率：          60 FPS
partial UI 更新上限：     30 次/s
```

禁止在 WPF UI Thread 做：

- ASR 二进制协议解析。
- WebSocket 网络 IO。
- 大量 JSON 解析。
- 音频重采样。
- 大块 Buffer 复制。
- 文本注入等待。

---

## 4.3 Finalization + 上屏

目标：

```text
Right Alt 松开 -> 进入 Finalizing：
几乎立即

收到 final -> 开始本地上屏：
目标 < 50 ms

上屏本身：
短文本通常几十毫秒以内
```

不得出现：

- 窗口闪烁。
- 目标程序失焦。
- 鼠标移动。
- 光标跳动。
- 任务栏闪烁。
- 主窗口弹出。
- 输入框卡顿。
- 人为逐字慢速输入。

---

# 5. 最低冷启动与零丢字设计

这一节是 MVP 核心。

目标不是“按键后尽快开始录音”。

目标是：

> 用户按下 Right Alt 后可以立刻开口，第一个字、第一个音节都应该尽可能被采集，不允许因为麦克风初始化、音频设备启动、WebSocket 建连、ASR Session 创建而丢失开头。

---

## 5.1 必须区分两类冷启动

### A. 应用首次启动后的第一次录音

通常最慢，可能包含：

```text
首次加载 NAudio
首次打开 WASAPI
首次创建 Resampler
首次加载 Credential
首次 DNS
首次 TLS
首次 WebSocket
首次 JIT
首次协议对象初始化
```

必须专门优化。

### B. 应用已经运行后的后续录音

要求明显更快。

这两者必须分别测量。

---

# 6. 麦克风策略：不长期独占，但允许轻量预热

MVP 默认不能一直占用麦克风。

Idle 时：

```text
麦克风必须关闭
```

但是为了减少首次冷启动，允许在应用启动完成后执行一次：

```text
Audio Warm-up
```

建议流程：

```text
应用启动
    ↓
延迟约 1~2 秒
    ↓
低优先级初始化音频组件
    ↓
枚举麦克风
    ↓
创建音频格式转换器
    ↓
可选：短暂打开默认设备
    ↓
立即关闭
```

目的：

- 触发必要 DLL/JIT。
- 获取默认设备信息。
- 提前创建常用对象。
- 避免第一次 Right Alt 才首次执行大量初始化。

注意：

- Warm-up 不得长期占用麦克风。
- Warm-up 如果导致系统隐私提示、设备冲突或明显 CPU 峰值，则只做无设备占用初始化。
- Warm-up 必须可失败。
- Warm-up 失败不得影响用户后续正常使用。

---

# 7. 零丢字核心：本地预缓冲必须先于 ASR 建连

绝对禁止如下错误流程：

```text
Right Alt DOWN
    ↓
等待 WebSocket 建连
    ↓
等待 ASR Session Ready
    ↓
然后才打开麦克风
```

这会直接丢用户开头语音。

正确流程必须是：

```text
Right Alt DOWN
    ↓
立即打开麦克风
    ↓
音频立刻进入本地 Ring Buffer / Channel
    ↓
与此同时启动 ASR WebSocket
    ↓
ASR Ready
    ↓
先发送已缓存的开头音频
    ↓
再无缝接实时音频
```

---

# 8. 音频启动优先级

Right Alt DOWN 后优先级：

```text
Priority 1：记录 Session / TargetWindow
Priority 2：启动 Audio Capture
Priority 3：开始本地预缓冲
Priority 4：启动 ASR Connection
Priority 5：显示 Overlay
```

注意：

Overlay 不是音频采集前置条件。

即使 Overlay 尚未绘制出来：

```text
麦克风也必须已经开始采集
```

用户体验原则：

> 用户不需要看到“正在聆听”之后才能开始说话。

按键本身就是开始说话的信号。

---

# 9. Pre-roll / Ring Buffer

必须实现短时本地音频预缓冲。

推荐：

```text
500 ms ~ 1000 ms
```

MVP 默认建议：

```text
750 ms
```

即：

```text
16,000 samples/s
× 2 bytes
× 1 channel
× 0.75 s
≈ 24 KB
```

内存开销极低。

作用：

- 覆盖 Audio Device Start 延迟。
- 覆盖 ASR WebSocket/TLS 建连延迟。
- 覆盖 ASR Session 初始化延迟。
- 避免开口第一字丢失。

实现建议：

```text
固定容量 Ring Buffer
```

而不是无限 Queue。

---

# 10. 音频生产者消费者模型

推荐结构：

```text
WASAPI Callback
      ↓
PCM Convert / Resample
      ↓
Fixed Ring Buffer
      ↓
Bounded Channel
      ↓
ASR Sender Loop
```

要求：

- Callback 不允许 await WebSocket。
- Callback 不允许等锁很久。
- Callback 不允许进行复杂 UI 更新。
- Callback 不允许大量分配对象。
- ASR 未 Ready 时，音频继续进入短期本地缓冲。
- ASR Ready 后先 flush pre-roll，再进入实时模式。

---

# 11. 首次录音冷启动优化

应用启动后可以做：

```text
JIT Warm-up
Protocol Warm-up
Audio Format Warm-up
Credential Warm-up
DNS Warm-up
```

允许：

- 提前加载程序集。
- 创建但不连接 ClientWebSocket。
- 提前读取 Credential。
- 提前解析设置。
- 预构造协议 Header 模板。
- 预构造 PCM Converter。
- 枚举音频设备。
- 缓存默认设备 ID。

不建议：

```text
Idle 时一直保持火山 WebSocket
```

原因：

- 长连接会有网络心跳。
- 增加资源占用。
- Provider 连接可能超时。
- 增加异常状态管理。
- 与“Idle 时几乎无活动”的设计冲突。

默认方案：

```text
Audio 本地 warm-up
+
每次录音建立 ASR WebSocket
+
利用 Pre-roll 消除建连导致的丢字
```

---

# 12. 后续录音启动优化

第一次成功录音后，可以复用：

- 音频设备配置。
- 默认麦克风 ID。
- Resampler 参数。
- 序列化配置对象。
- Provider Endpoint。
- Header 模板。
- 认证字段。
- ArrayPool。
- Ring Buffer 实例或对象池。

每次录音仍然要求：

```text
麦克风关闭 -> Right Alt -> 快速打开
```

不长期独占设备。

---

# 13. 冷启动性能指标

必须记录两个指标：

### 首次录音

```text
App 启动后首次 Right Alt
-> Audio Capture First Frame
```

目标：

```text
< 100 ms
```

最好：

```text
< 60 ms
```

### 后续录音

```text
Right Alt
-> Audio Capture First Frame
```

目标：

```text
< 60 ms
```

最好：

```text
< 30~40 ms
```

如果具体机器达不到，必须记录实际值，而不是伪造达标。

---

# 14. 零丢字验收

必须人工测试：

连续说：

```text
今天下午帮我分析一下数据
```

操作方式：

```text
Right Alt DOWN 的同时立即开始说“今天”
```

重复：

```text
50 次
```

要求：

- “今天”的“今”不得系统性丢失。
- 开头 1~3 个汉字不得高频缺失。
- 不得要求用户先按住半秒再说话。

额外测试：

```text
Right Alt DOWN
与第一个音节间隔 < 50 ms
```

仍应尽量完整。

---

# 15. 架构

整体模块：

```text
VoiceTyper.App
│
├── 应用生命周期
├── DI
├── Tray
├── Settings
├── Overlay
└── Startup Warm-up
        │
        ▼
VoiceTyper.Core
│
├── DictationCoordinator
├── DictationStateMachine
├── Session
├── PerformanceMetrics
└── Interfaces
        │
        ├────────────────────┐
        ▼                    ▼
VoiceTyper.Windows       VoiceTyper.Volcengine
│                       │
├── Keyboard Hook       ├── WebSocket
├── Audio Capture       ├── Seed Protocol
├── Audio Warm-up       ├── Request Encoder
├── Ring Buffer         ├── Response Decoder
├── Foreground Window   └── ASR Mapping
├── Text Injection
├── Clipboard
└── Credential Store
```

---

# 16. 解决方案目录结构

```text
VoiceTyper/
├── VoiceTyper.sln
├── README.md
├── DEVELOPMENT.md
├── docs/
│   ├── volcengine-notes.md
│   ├── performance.md
│   └── compatibility.md
│
├── src/
│   ├── VoiceTyper.App/
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── Bootstrap/
│   │   │   └── ServiceRegistration.cs
│   │   ├── Warmup/
│   │   │   └── StartupWarmupService.cs
│   │   ├── Tray/
│   │   │   └── TrayIconService.cs
│   │   ├── Overlay/
│   │   │   ├── RecordingOverlay.xaml
│   │   │   ├── RecordingOverlay.xaml.cs
│   │   │   └── RecordingOverlayViewModel.cs
│   │   └── Settings/
│   │       ├── SettingsWindow.xaml
│   │       ├── SettingsWindow.xaml.cs
│   │       └── SettingsViewModel.cs
│   │
│   ├── VoiceTyper.Core/
│   │   ├── Dictation/
│   │   │   ├── DictationCoordinator.cs
│   │   │   ├── DictationState.cs
│   │   │   ├── DictationSession.cs
│   │   │   └── DictationResult.cs
│   │   ├── Performance/
│   │   │   ├── IPerformanceMetrics.cs
│   │   │   └── SessionPerformanceMetrics.cs
│   │   ├── Audio/
│   │   │   ├── IAudioCaptureService.cs
│   │   │   ├── IAudioWarmupService.cs
│   │   │   ├── AudioChunk.cs
│   │   │   ├── AudioLevel.cs
│   │   │   └── AudioInputDevice.cs
│   │   ├── Hotkeys/
│   │   ├── Speech/
│   │   ├── Input/
│   │   ├── Overlay/
│   │   ├── Settings/
│   │   └── Diagnostics/
│   │
│   ├── VoiceTyper.Windows/
│   │   ├── Keyboard/
│   │   ├── Audio/
│   │   │   ├── WasapiAudioCaptureService.cs
│   │   │   ├── AudioWarmupService.cs
│   │   │   ├── PcmRingBuffer.cs
│   │   │   └── PcmResampler.cs
│   │   ├── Input/
│   │   ├── Security/
│   │   └── Startup/
│   │
│   └── VoiceTyper.Volcengine/
│       ├── VolcengineStreamingRecognizer.cs
│       ├── VolcengineOptions.cs
│       ├── Protocol/
│       └── Models/
│
└── tests/
    ├── VoiceTyper.Core.Tests/
    ├── VoiceTyper.Windows.Tests/
    └── VoiceTyper.Volcengine.Tests/
```

---

# 17. 技术栈

必须：

- .NET 8
- C# 12
- WPF
- x64
- Nullable Enabled
- Implicit Usings
- ClientWebSocket
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Options
- NAudio
- CommunityToolkit.Mvvm

尽量减少依赖数量。

禁止因为 UI 方便引入大型第三方 UI 框架。

---

# 18. 状态机

```csharp
public enum DictationState
{
    Idle,
    Starting,
    Recording,
    Finalizing,
    Injecting,
    Failed
}
```

状态转换：

```text
Idle
 └─ RightAltDown -> Starting

Starting
 ├─ AudioReady -> Recording
 └─ Error -> Failed

Recording
 ├─ RightAltUp -> Finalizing
 └─ Error -> Failed

Finalizing
 ├─ FinalText -> Injecting
 ├─ EmptyFinal -> Idle
 └─ Error -> Failed

Injecting
 ├─ Success -> Idle
 └─ Error -> Failed

Failed
 └─ Cleanup -> Idle
```

注意：

`Starting` 不代表“还没有录音”。

正确行为：

```text
Starting 阶段就必须尽快开始音频采集
```

不得等 ASR Ready 才进入音频采集。

---

# 19. 核心接口

## 19.1 音频

```csharp
public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioChunk>? AudioAvailable;
    event EventHandler<AudioLevel>? LevelChanged;

    Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(
        CancellationToken cancellationToken);

    Task StartAsync(
        string? deviceId,
        CancellationToken cancellationToken);

    Task StopAsync(
        CancellationToken cancellationToken);
}
```

```csharp
public interface IAudioWarmupService
{
    Task WarmupAsync(CancellationToken cancellationToken);
}
```

```csharp
public sealed record AudioChunk(
    ReadOnlyMemory<byte> Pcm16,
    DateTimeOffset CapturedAt);
```

---

## 19.2 Speech

```csharp
public interface IStreamingSpeechRecognizer : IAsyncDisposable
{
    event EventHandler<SpeechRecognitionEvent>? RecognitionUpdated;

    Task StartAsync(
        SpeechRecognitionOptions options,
        CancellationToken cancellationToken);

    ValueTask SendAudioAsync(
        ReadOnlyMemory<byte> pcm16,
        CancellationToken cancellationToken);

    Task<DictationResult> CompleteAsync(
        CancellationToken cancellationToken);

    Task AbortAsync(
        CancellationToken cancellationToken);
}
```

---

# 20. 实施任务

---

## Task 1 — 创建解决方案和 Core Contract

目标：

- 创建完整 Solution。
- 建立项目边界。
- 创建核心接口和 Record。
- 不实现业务。

验收：

```powershell
dotnet restore
dotnet build VoiceTyper.sln -c Debug
dotnet test VoiceTyper.sln -c Debug
```

Commit：

```text
chore: 初始化 VoiceTyper 解决方案和核心接口
```

---

## Task 2 — Settings 与安全 Credential

实现：

- settings.json。
- DPAPI 或 Windows Credential Manager。
- 禁止 secret 明文。
- Atomic Save。

测试：

- 默认值。
- JSON 损坏。
- roundtrip。
- secret 不出现在 JSON。

Commit：

```text
feat: 实现安全配置和凭据存储
```

---

## Task 3 — Right Alt 低级 Hook

使用：

```text
WH_KEYBOARD_LL
VK_RMENU
```

实现：

- KeyDown。
- KeyUp。
- Auto-repeat 抑制。
- Hook Dispose。

测试：

按住 5 秒只产生：

```text
Pressed
Released
```

各一次。

Commit：

```text
feat: 实现 Right Alt 全局按住说话
```

---

## Task 4 — Target Window Capture

Right Alt DOWN 后：

第一时间获取：

- HWND
- PID
- ProcessName
- Title

必须在 Overlay 出现前获取。

Commit：

```text
feat: 捕获语音输入目标窗口
```

---

## Task 5 — Audio Warm-up

目标：

优化首次录音。

实现：

- StartupWarmupService。
- 枚举默认设备。
- 初始化 Audio Format。
- 初始化 Resampler。
- 触发必要 JIT。
- 可配置的轻量设备 warm-up。

要求：

- 不长期占麦克风。
- Warm-up 完成后必须释放设备。
- 失败不影响主流程。

测量：

```text
首次录音 Audio First Frame 延迟
```

Commit：

```text
perf: 增加音频首次启动预热
```

---

## Task 6 — PCM Ring Buffer 和 Pre-roll

目标：

解决开口丢字。

实现：

```text
固定容量 PCM Ring Buffer
默认 750 ms
```

要求：

- 固定内存。
- 不无限增长。
- 支持按时间顺序读取。
- 支持 flush。
- 支持 Session Reset。

单元测试：

- 写入。
- wrap-around。
- 顺序读取。
- 容量覆盖。
- reset。

Commit：

```text
feat: 增加零丢字音频预缓冲
```

---

## Task 7 — WASAPI Audio Capture

输出：

```text
PCM
16 kHz
16-bit
Mono
```

要求：

- Right Alt 后立即 Start。
- Audio Callback 不等网络。
- PCM 先进入 pre-roll / bounded channel。
- 50 次 start/stop 不锁设备。

Commit：

```text
feat: 实现低延迟麦克风采集
```

---

## Task 8 — 冷启动性能 Instrumentation

记录：

```text
Hotkey Timestamp
Audio Start Requested
First Audio Frame
ASR Connect Start
ASR Connected
First Audio Sent
First Partial
RightAlt Released
Final Received
Injection Start
Injection End
```

输出到调试日志和 performance report。

Commit：

```text
perf: 增加语音输入端到端延迟测量
```

---

## Task 9 — 火山 Seed Protocol Codec

实现前必须阅读当前官方文档：

```text
https://www.volcengine.com/docs/6561/1354867
https://www.volcengine.com/docs/6561/1395846
```

独立实现：

- Header。
- Encoder。
- Decoder。
- Sequence。
- Final。
- Error。

Commit：

```text
feat: 实现火山 Seed 协议
```

---

## Task 10 — 火山 WebSocket Streaming ASR

当前 Endpoint：

```text
wss://openspeech.bytedance.com/api/v3/sauc/bigmodel
```

要求：

- Connect 并行于音频采集。
- 不阻塞音频。
- ASR Ready 后优先 flush pre-roll。
- pre-roll 与实时音频顺序严格正确。
- partial 只发 UI。
- final 只返回一次。

Commit：

```text
feat: 实现火山流式语音识别
```

---

## Task 11 — 零丢字端到端逻辑

目标：

完成：

```text
Right Alt
-> 立即采集
-> 本地预缓冲
-> ASR Connect
-> Flush Pre-roll
-> Realtime
```

测试：

模拟 ASR 连接延迟：

```text
0 ms
100 ms
300 ms
800 ms
```

确认前部 PCM 不丢失。

Commit：

```text
feat: 完成零丢字流式音频管线
```

---

## Task 12 — Recording Overlay

要求：

- 不抢焦点。
- Topmost。
- No Taskbar。
- No Activate。
- Click-through。
- Bottom Center。

注意：

Overlay 显示不得阻塞音频启动。

Commit：

```text
feat: 实现无焦点录音悬浮窗
```

---

## Task 13 — 流畅低开销 Waveform

原则：

- 不做 FFT。
- 使用 RMS / Peak。
- Fixed Ring Buffer。
- 60 FPS 上限。
- 平滑算法。
- 隐藏后立即停止渲染。
- 避免 per-frame allocation。

建议：

```text
display = previous + alpha * (incoming - previous)
```

要求：

- 语音开始时反应快。
- 停止时平滑衰减。
- 不能抖动。
- 60 秒录音无持续内存增长。

Commit：

```text
feat: 实现低开销流畅录音波形
```

---

## Task 14 — Unicode SendInput

要求：

- KEYEVENTF_UNICODE。
- INPUT[] 批量发送。
- 不逐字符 P/Invoke。
- 不 sleep。
- 中文无乱码。

测试：

```text
你好，这是 VoiceTyper 的中文输入测试 123。
```

Commit：

```text
feat: 实现 Unicode 无感上屏
```

---

## Task 15 — Clipboard Fallback

要求：

- 保存 Clipboard。
- Set Text。
- Ctrl+V。
- Best-effort restore。
- 禁止阻塞 UI。
- 禁止无限重试。

Commit：

```text
feat: 增加剪贴板输入 fallback
```

---

## Task 16 — DictationCoordinator

集成：

- Hotkey。
- TargetWindow。
- Audio。
- Pre-roll。
- ASR。
- Overlay。
- Injection。

必须测试：

- Happy Path。
- Duplicate KeyDown。
- ASR Failure。
- Empty Final。
- Stale Callback。
- Invalid Target。
- Delayed ASR Connect。
- Pre-roll Flush。
- Session Cancel。

Commit：

```text
feat: 完成语音输入状态机和主流程
```

---

## Task 17 — Tray / Single Instance

实现：

- Tray。
- Enable。
- Settings。
- Exit。
- Named Mutex。

Exit 必须释放：

- Hook。
- Audio。
- ASR。
- Timer。
- Tray。

Commit：

```text
feat: 完成托盘和单实例生命周期
```

---

## Task 18 — Settings UI

包含：

### General

- Enable。
- Start with Windows。
- Show Partial。

### Microphone

- Default。
- Device List。
- Level Test。

### Volcengine

- App ID。
- Token / API Key。
- Resource ID。
- Test Connection。

Commit：

```text
feat: 增加 VoiceTyper 设置界面
```

---

## Task 19 — 性能与冷启动专项 Profiling

必须测试：

```text
1. App 启动后的首次录音
2. 第二次录音
3. 连续 100 次录音
4. Idle 5 分钟
5. 60 秒静音录音
6. 60 秒正常说话 + partial
7. ASR Connect 延迟模拟
8. Final + Injection
```

记录：

```text
CPU
Working Set
Private Bytes
Hotkey -> First Audio Frame
Hotkey -> Overlay
Hotkey -> ASR Connect
Hotkey -> First Audio Sent
Hotkey -> First Partial
Release -> Final
Final -> Injection
```

特别验收：

```text
首次录音：
Hotkey -> First Audio Frame < 100 ms 目标

后续录音：
Hotkey -> First Audio Frame < 60 ms 目标
```

并人工测试：

```text
按下 Right Alt 的同时立刻说“今天”
连续 50 次
```

不得系统性丢“今”。

写入：

```text
docs/performance.md
```

Commit：

```text
perf: 验证低资源和零丢字性能预算
```

---

## Task 20 — 兼容性测试

应用：

- Notepad。
- Chrome。
- Edge。
- VS Code。
- Word。
- Electron App。

测试内容：

- 5 秒中文。
- 30 秒中文。
- 中英文混合。
- 数字。
- 标点。
- 快速连续输入。
- 用户输入同时切应用。
- 失败恢复。

写入：

```text
docs/compatibility.md
```

Commit：

```text
test: 验证 Windows 应用输入兼容性
```

---

## Task 21 — Release Build

```powershell
dotnet publish src/VoiceTyper.App/VoiceTyper.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true
```

要求：

在无开发 SDK 的 Windows 环境测试。

最终验证：

- Tray。
- Right Alt。
- First Audio Frame。
- Waveform。
- ASR。
- Final。
- Injection。
- Exit Cleanup。

Commit：

```text
build: 准备 Windows x64 正式发布版本
```

---

# 21. 波形专项设计

录音波形必须：

- 流畅。
- 好看。
- CPU 低。
- GC 压力低。

不做：

```text
FFT
频谱分析
复杂 Shader
大量动画控件
```

推荐：

```text
Audio callback
    ↓
Peak / RMS
    ↓
固定 Ring Buffer
    ↓
60Hz UI Render
    ↓
Smoothed Bars / Polyline
```

WPF 建议：

- 单个自定义 DrawingVisual。
- 尽量减少 Layout Pass。
- 不反复创建 Rectangle。
- 不每帧重新 Build 完整 Visual Tree。
- Brush / Pen 可 Freeze 时 Freeze。

Overlay Hide 后：

```text
Render Loop 必须停止
```

---

# 22. 上屏专项设计

Primary：

```text
SendInput + KEYEVENTF_UNICODE
```

Fallback：

```text
Clipboard + Ctrl+V
```

Primary 要求：

- 一次构造 INPUT[]。
- 尽可能少 Win32 Call。
- 不人为 sleep。
- 不逐字慢慢输入。
- 不移动鼠标。
- 不抢焦点。
- 不弹窗。
- 不触发 Enter。
- 不额外加空格。

当原目标仍有焦点时：

```text
禁止多余 SetForegroundWindow
```

只有确实失焦才尝试恢复。

---

# 23. 火山引擎

目标接口：

```text
wss://openspeech.bytedance.com/api/v3/sauc/bigmodel
```

官方文档：

```text
https://www.volcengine.com/docs/6561/1354867
https://www.volcengine.com/docs/6561/1354871
https://www.volcengine.com/docs/6561/1395846
```

注意：

在 Task 9 / Task 10 实现时，Codex 必须再次确认：

- 鉴权 Header。
- Resource ID。
- Seed Protocol。
- Compression。
- Sequence。
- Final。
- Error。
- Audio Format。

不得依赖过时第三方示例。

---

# 24. 失败处理

## 麦克风不可用

显示：

```text
未找到可用麦克风
```

## 麦克风权限

显示：

```text
无法访问麦克风
```

## 网络

显示：

```text
网络不可用，识别失败
```

## 火山鉴权失败

显示：

```text
火山引擎认证失败
```

## 超时

显示：

```text
识别超时，请重试
```

## Target 被关闭

显示：

```text
原输入窗口已关闭
```

## 上屏失败

显示：

```text
输入失败，文字已复制
```

所有错误必须：

```text
Cleanup
-> Idle
```

不得因为一次失败导致后台程序退出。

---

# 25. 日志

每个 Session 使用：

```text
SessionId
```

建议记录：

```text
Session Start
Target Captured
Audio Start Requested
First Audio Frame
ASR Connect Start
ASR Connected
Pre-roll Flushed
First Partial
RightAlt Released
Final Received
Injection Started
Injection Completed
Session Completed
```

默认不记录：

- Full Transcript。
- PCM。
- Token。
- API Key。
- 完整 Header。

---

# 26. 隐私

规则：

- 不保存音频文件。
- 不做 Analytics。
- 不做 Telemetry。
- 不持久化转写历史。
- 音频只发送到配置的火山引擎。
- 日志不记录 Secret。
- UI 中应说明语音会发送到火山引擎进行云识别。

---

# 27. 性能禁止项

Codex 禁止：

1. Keyboard Polling。
2. Idle 打开麦克风。
3. Idle 长期保持 ASR WebSocket。
4. Overlay Hide 后仍跑动画。
5. 高频 DispatcherTimer。
6. Audio Callback 直接网络 Send。
7. Unbounded Queue。
8. Clipboard Monitoring。
9. Full-screen 透明 WPF Window。
10. Foreground Window Polling。
11. 音频每帧大数组分配。
12. UI Thread 做 Protocol Decode。
13. 每个字符一个 SendInput P/Invoke。
14. 人为逐字符 Sleep。
15. MVP 中持续跑 UI Automation。
16. 为视觉效果加入 FFT。
17. 录音完成后继续保留 Session Buffer。

---

# 28. Definition of Done

只有全部满足才算 MVP 完成：

- [ ] Release Build 成功。
- [ ] 自动化测试通过。
- [ ] Right Alt 全局工作。
- [ ] Key Repeat 不产生重复 Session。
- [ ] 首次按键后立即讲话不系统性丢开头。
- [ ] 后续录音启动明显更快。
- [ ] 首次 Hotkey -> First Audio Frame 已测量。
- [ ] 后续 Hotkey -> First Audio Frame 已测量。
- [ ] PCM 在 ASR 边界为 16kHz / 16-bit / Mono。
- [ ] Pre-roll 正确工作。
- [ ] ASR Connect 期间音频不丢失。
- [ ] partial 只显示。
- [ ] final 只上屏一次。
- [ ] 原 Target Window 被正确尊重。
- [ ] Notepad 中文输入正常。
- [ ] Chrome 正常。
- [ ] Edge 正常。
- [ ] VS Code 正常。
- [ ] Clipboard Fallback 正常。
- [ ] Overlay 不抢焦点。
- [ ] Waveform 流畅。
- [ ] Waveform Hide 后完全停止渲染。
- [ ] Idle 不占麦克风。
- [ ] Idle 无 ASR Socket。
- [ ] Idle CPU 满足目标或有正式测量说明。
- [ ] 录音 CPU 已测量。
- [ ] 100 次 Session 无持续内存增长。
- [ ] Final 上屏没有明显焦点闪烁。
- [ ] SendInput 使用批量 INPUT[]。
- [ ] Credential 不出现在 settings.json。
- [ ] 日志不泄露 Credential。
- [ ] 单实例正常。
- [ ] Exit 释放 Hook / Audio / Socket。
- [ ] docs/performance.md 完整。
- [ ] docs/compatibility.md 完整。
- [ ] 在无 .NET SDK 机器可运行。

---

# 29. 给 Codex 的第一条 Prompt

将本文保存为：

```text
DEVELOPMENT.md
```

放项目根目录。

然后给 Codex：

> 完整阅读根目录 `DEVELOPMENT.md`，把它当作本项目唯一权威规格。不要一次开发整个软件。现在只执行 Task 1：创建解决方案和 Core Contract。编码前先总结 Task 1 的范围、准备创建的文件，以及你发现的任何规格冲突。然后实现 Task 1，执行 restore、build、test，报告准确结果和修改文件，最后停止。没有我的明确批准，不要进入 Task 2。

之后每个任务使用：

> 继续执行 `DEVELOPMENT.md` 的 Task N，只做这个 Task。开始前先检查现有仓库和上一任务已经定义的接口。严格控制范围，实现后运行该 Task 要求的测试，报告结果和修改文件，然后停止，不要自动开始下一 Task。

---

# 30. 最终架构原则

最重要的原则：

```text
不是 Windows IME
    ↓
是轻量全局语音输入工具

Right Alt
    ↓
立即采集音频
    ↓
先进入本地 Pre-roll
    ↓
ASR 同时建立连接
    ↓
ASR Ready 后 Flush Pre-roll
    ↓
继续实时 Streaming

说话过程中
    ↓
Partial -> Overlay

松开
    ↓
Final -> 原输入窗口
    ↓
只上屏一次

Idle
    ↓
无麦克风
无 Socket
无动画
无轮询
CPU 接近 0

任何失败
    ↓
不注入 Partial
    ↓
完整 Cleanup
    ↓
回到 Idle
```

如果底层实现与这些原则冲突：

```text
优先保留原则
修改底层实现
```

尤其禁止为了代码简单而采用：

```text
按键
-> 等麦克风准备
-> 等 ASR 建连
-> 然后开始收音
```

正确顺序永远是：

```text
按键
-> 立即收音
-> 本地缓存
-> 后台建 ASR
-> Flush 开头音频
```

这条规则是防止“按下后前几个字丢失”的核心。
