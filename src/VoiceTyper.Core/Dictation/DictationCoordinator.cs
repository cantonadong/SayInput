using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Input;
using VoiceTyper.Core.Overlay;
using VoiceTyper.Core.Performance;
using VoiceTyper.Core.Speech;

namespace VoiceTyper.Core.Dictation;

public sealed class DictationCoordinator(Func<IAudioCaptureService> createAudio, Func<IStreamingSpeechRecognizer> createSpeech,
    IForegroundWindowService windows, IRecordingOverlay overlay, ITextInjectionService injection, IPerformanceMetrics metrics) : IAsyncDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource? lifetime;
    private TaskCompletionSource? release;
    private Task completion = Task.CompletedTask;
    private Guid active;
    private bool disposed;
    private volatile DictationState state;
    public DictationState State => state;
    public string? MicrophoneDeviceId { get; set; }
    public Task Completion { get { lock (gate) return completion; } }
    public event Action<Guid, DictationState>? StateChanged;
    public event Action<SessionPerformanceMetrics, string?, string?>? Finished;

    public bool TryStart()
    {
        lock (gate)
        {
            if (disposed || active != Guid.Empty) return false;
            var id = Guid.NewGuid();
            metrics.Mark(id, PerformanceEvent.HotkeyPressed);
            var target = windows.Capture();
            if (target is null) { metrics.Clear(id); return false; }
            metrics.Mark(id, PerformanceEvent.TargetCaptured);
            active = id;
            lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var token = lifetime.Token;
            var released = release.Task;
            SetState(id, DictationState.Starting);
            completion = Task.Run(() => RunAsync(id, target, released, MicrophoneDeviceId, token));
            return true;
        }
    }
    public void Release()
    {
        lock (gate)
        {
            if (active == Guid.Empty || release!.Task.IsCompleted) return;
            metrics.Mark(active, PerformanceEvent.HotkeyReleased);
            release.TrySetResult();
            if (state == DictationState.Recording) SetState(active, DictationState.Finalizing);
        }
    }
    public Task CancelAsync()
    {
        lock (gate) { lifetime?.Cancel(); return completion; }
    }
    private async Task RunAsync(Guid id, TargetWindow target, Task released, string? deviceId, CancellationToken ct)
    {
        string? failure = null, final = null;
        IAudioCaptureService? audio = null;
        IStreamingSpeechRecognizer? speech = null;
        void Partial(object? sender, SpeechRecognitionEvent update)
        {
            if (update.SessionId != id || ct.IsCancellationRequested) return;
            metrics.Mark(id, PerformanceEvent.FirstPartial);
            overlay.UpdatePartial(id, update.Text);
        }
        void Level(object? sender, AudioLevel level) => overlay.UpdateLevel(id, level);
        try
        {
            audio = createAudio();
            speech = createSpeech();
            audio.LevelChanged += Level;
            speech.RecognitionUpdated += Partial;
            var pipeline = new AudioPipeline(audio, speech, metrics, id);
            await pipeline.RunAsync(deviceId, released, () =>
            {
                SetState(id, released.IsCompleted ? DictationState.Finalizing : DictationState.Recording);
                overlay.Show(id);
                metrics.Mark(id, PerformanceEvent.OverlayShown);
            }, ct).ConfigureAwait(false);
            SetState(id, DictationState.Finalizing);
            final = (await speech.CompleteAsync(ct).ConfigureAwait(false)).Text;
            metrics.Mark(id, PerformanceEvent.FinalReceived);
            if (!string.IsNullOrWhiteSpace(final))
            {
                ct.ThrowIfCancellationRequested();
                if (!windows.IsValid(target)) throw new InvalidOperationException("原输入窗口已关闭，请从设置窗口复制本次文字。");
                SetState(id, DictationState.Injecting);
                metrics.Mark(id, PerformanceEvent.InjectionStarted);
                var result = await injection.InjectAsync(target, final, ct).ConfigureAwait(false);
                metrics.Mark(id, PerformanceEvent.InjectionCompleted);
                if (!result.Succeeded) throw new InvalidOperationException(result.ErrorMessage ?? "输入失败，请从设置窗口复制本次文字。");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            // Adapter messages are bounded and credential-free; never include arbitrary provider response bodies.
            failure = error is InvalidOperationException or IOException or TimeoutException
                ? error.Message : "录音或识别失败，请检查麦克风、网络和凭据后重试。";
            SetState(id, DictationState.Failed);
        }
        finally
        {
            if (speech is not null)
            {
                speech.RecognitionUpdated -= Partial;
                try { await speech.DisposeAsync().ConfigureAwait(false); } catch { failure ??= "识别连接清理失败，请重试。"; }
            }
            if (audio is not null)
            {
                audio.LevelChanged -= Level;
                try { await audio.DisposeAsync().ConfigureAwait(false); } catch { failure ??= "麦克风清理失败，请检查设备。"; }
            }
            if (failure is null) overlay.Hide(id); else overlay.ShowError(id, failure);
            metrics.Mark(id, PerformanceEvent.SessionCompleted);
            var snapshot = metrics.Snapshot(id);
            metrics.Clear(id);
            lock (gate)
            {
                active = Guid.Empty;
                release = null;
                lifetime?.Dispose(); lifetime = null;
                SetState(id, DictationState.Idle);
            }
            Finished?.Invoke(snapshot, failure, failure is null ? null : final);
        }
    }
    private void SetState(Guid id, DictationState value) { state = value; StateChanged?.Invoke(id, value); }
    public async ValueTask DisposeAsync()
    {
        lock (gate) disposed = true;
        await CancelAsync().ConfigureAwait(false);
    }
}
