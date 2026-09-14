namespace VoiceTyper.Core.Audio;

public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<AudioChunk>? AudioAvailable;
    event EventHandler<AudioLevel>? LevelChanged;
    Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken);
    Task StartAsync(string? deviceId, CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
