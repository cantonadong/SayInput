namespace VoiceTyper.Core.Overlay;

/// <summary>Implementations marshal UI updates without blocking audio capture.</summary>
public interface IRecordingOverlay
{
    void Show(Guid sessionId);
    void UpdatePartial(Guid sessionId, string text);
    void UpdateLevel(Guid sessionId, Audio.AudioLevel level);
    void ShowError(Guid sessionId, string message);
    void Hide(Guid sessionId);
}
