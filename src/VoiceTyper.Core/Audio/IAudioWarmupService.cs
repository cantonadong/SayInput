namespace VoiceTyper.Core.Audio;

public interface IAudioWarmupService
{
    Task WarmupAsync(CancellationToken cancellationToken);
}
