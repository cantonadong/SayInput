using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Overlay;
using System.Windows;
using System.Windows.Threading;

namespace VoiceTyper.App.Overlay;

public sealed class LazyRecordingOverlay : IRecordingOverlay
{
    private readonly object gate = new();
    private readonly Func<IRecordingOverlay> factory;
    private readonly Dispatcher dispatcher;
    private IRecordingOverlay? instance;
    public bool ShowPartial { get; set; } = true;

    public LazyRecordingOverlay(Func<IRecordingOverlay>? factory = null, Dispatcher? dispatcher = null)
    {
        this.factory = factory ?? (() => new RecordingOverlay());
        this.dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    private IRecordingOverlay Get()
    {
        lock (gate)
        {
            instance ??= factory();
            if (instance is RecordingOverlay window) window.ShowPartial = ShowPartial;
            return instance;
        }
    }

    public void Show(Guid sessionId)
    {
        if (dispatcher.CheckAccess()) Get().Show(sessionId);
        else dispatcher.Invoke(() => Get().Show(sessionId));
    }
    public void UpdatePartial(Guid sessionId, string text) { lock (gate) instance?.UpdatePartial(sessionId, text); }
    public void UpdateLevel(Guid sessionId, AudioLevel level) { lock (gate) instance?.UpdateLevel(sessionId, level); }
    public void Hide(Guid sessionId) { lock (gate) instance?.Hide(sessionId); }
    public void ShowError(Guid sessionId, string text) { lock (gate) instance?.ShowError(sessionId, text); }
    public void Finalizing(Guid sessionId) { lock (gate) if (instance is RecordingOverlay window) window.Finalizing(sessionId); }
    public void Close()
    {
        void CloseCore() { lock (gate) { if (instance is RecordingOverlay window) window.Close(); instance = null; } }
        if (dispatcher.CheckAccess()) CloseCore();
        else dispatcher.Invoke(CloseCore);
    }
}
