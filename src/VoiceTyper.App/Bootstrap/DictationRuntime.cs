using VoiceTyper.App.Overlay;
using VoiceTyper.App.Settings;
using VoiceTyper.Core.Dictation;
using VoiceTyper.Core.Performance;
using VoiceTyper.Volcengine;
using VoiceTyper.Windows.Audio;
using VoiceTyper.Windows.Input;
using VoiceTyper.Windows.Keyboard;
using VoiceTyper.App.Sound;

namespace VoiceTyper.App.Bootstrap;

public sealed class DictationRuntime : IAsyncDisposable
{
    private readonly RightAltHotkeyService hotkey = new();
    private readonly ConfigurationService configuration;
    private int suspensions;
    private volatile bool enabled;
    private bool disposed;
    private readonly WaveSoundCuePlayer sounds = new();
    private readonly ToggleDictationController toggle;
    private Task toggleTask = Task.CompletedTask;
    public LazyRecordingOverlay Overlay { get; } = new();
    public DictationCoordinator Coordinator { get; }
    public event Action<string, string?>? Notice;
    public string? RecoveryText { get; private set; }
    public string? LastError { get; private set; }
    public string MetricsText { get; private set; } = "尚无语音会话测量。";
    public event Action? MetricsUpdated;

    public DictationRuntime(ConfigurationService configuration)
    {
        this.configuration = configuration;
        var windows = new WindowsForegroundWindowService();
        var archive = new WaveRecordingArchive(configuration.Paths.RecordingDirectory);
        _ = archive.CleanupAsync();
        Coordinator = new(() => new ArchivingAudioCaptureService(archive),
            () => new VolcengineStreamingRecognizer(configuration.Provider), windows, Overlay,
            new WindowsTextInjectionService(windows), new PerformanceMetrics());
        toggle = new(() => Coordinator.State, StartSession, Coordinator.Release,
            new SystemOutputMuteService(), sounds, Coordinator.CancelAsync);
        hotkey.Pressed += (_, _) =>
        {
            if (!enabled || suspensions > 0) return;
            if (!configuration.HasCredentials) { sounds.Play(SoundCue.Error); Notice?.Invoke("请先在设置中保存火山引擎凭据。", null); return; }
            toggleTask = toggle.ToggleAsync();
        };
        hotkey.Cancelled += (_, _) =>
        {
            if (!enabled || suspensions > 0) return;
            toggleTask = toggle.CancelAsync();
        };
        Coordinator.StateChanged += (id, state) =>
        {
            hotkey.CancelEnabled = state is DictationState.Starting or DictationState.Recording;
            if (state == DictationState.Finalizing) Overlay.Finalizing(id);
        };
        Coordinator.Finished += (metrics, outcome, error, text) =>
        {
            string Time(PerformanceEvent marker) => metrics.Milestones.TryGetValue(marker, out var value) ? $"{value.TotalMilliseconds:F1} ms" : "未到达";
            MetricsText = $"最近会话：首帧 {Time(PerformanceEvent.FirstAudioFrame)} · 首次发送 {Time(PerformanceEvent.FirstAudioSent)} · 完成 {Time(PerformanceEvent.SessionCompleted)}";
            MetricsUpdated?.Invoke();
            toggleTask = toggle.CompleteAsync(outcome);
            if (error is not null) { LastError = error; RecoveryText = text; Notice?.Invoke(error, text); }
            else { LastError = null; RecoveryText = null; }
        };
    }

    private bool StartSession()
    {
        if (Coordinator.TryStart()) return true;
        if (Coordinator.State == DictationState.Idle)
        {
            sounds.Play(SoundCue.Error);
            Notice?.Invoke("请先点击其他应用的文本输入位置。", null);
        }
        return false;
    }

    public void Apply()
    {
        if (disposed) return;
        Coordinator.MicrophoneDeviceId = configuration.Preferences.MicrophoneDeviceId;
        Overlay.ShowPartial = configuration.Preferences.ShowPartial;
        enabled = configuration.Preferences.Enabled;
        UpdateHook();
    }
    public async Task SuspendAsync(bool value)
    {
        suspensions = Math.Max(0, suspensions + (value ? 1 : -1));
        UpdateHook();
        if (value) await Coordinator.CancelAsync();
    }
    private void UpdateHook()
    {
        if (disposed) return;
        if (enabled && suspensions == 0) hotkey.Start();
        else hotkey.Stop();
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        enabled = false;
        try { hotkey.Dispose(); }
        finally
        {
            try { try { await toggleTask.ConfigureAwait(false); } catch { } await toggle.StopAsync().ConfigureAwait(false); await Coordinator.DisposeAsync(); }
            finally { sounds.Dispose(); Overlay.Close(); }
        }
    }
}
