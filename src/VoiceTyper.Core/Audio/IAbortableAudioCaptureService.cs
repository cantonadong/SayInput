namespace VoiceTyper.Core.Audio;

public interface IAbortableAudioCaptureService
{
    Task AbortAsync();
}
