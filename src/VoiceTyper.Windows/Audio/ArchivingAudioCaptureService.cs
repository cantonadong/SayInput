using VoiceTyper.Core.Audio;

namespace VoiceTyper.Windows.Audio;

public sealed class ArchivingAudioCaptureService(WaveRecordingArchive archive) : IAudioCaptureService, IAbortableAudioCaptureService
{
    private readonly WasapiAudioCaptureService inner = new();
    private IWaveRecordingSession? session;
    public event EventHandler<AudioChunk>? AudioAvailable;
    public event EventHandler<AudioLevel>? LevelChanged;

    public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken) => inner.GetDevicesAsync(cancellationToken);

    public async Task StartAsync(string? deviceId, CancellationToken cancellationToken)
    {
        session = archive.BeginSession();
        inner.AudioAvailable += OnAudio;
        inner.LevelChanged += OnLevel;
        try { await inner.StartAsync(deviceId, cancellationToken).ConfigureAwait(false); }
        catch { await AbortSessionAsync().ConfigureAwait(false); throw; }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await inner.StopAsync(cancellationToken).ConfigureAwait(false);
        inner.AudioAvailable -= OnAudio;
        inner.LevelChanged -= OnLevel;
        var current = Interlocked.Exchange(ref session, null);
        if (current is not null) { await current.CompleteAsync().ConfigureAwait(false); await current.DisposeAsync(); }
    }

    private void OnAudio(object? sender, AudioChunk chunk)
    {
        session?.Write(chunk.Pcm16.Span);
        AudioAvailable?.Invoke(this, chunk);
    }
    private void OnLevel(object? sender, AudioLevel level) => LevelChanged?.Invoke(this, level);

    private async Task AbortSessionAsync()
    {
        inner.AudioAvailable -= OnAudio;
        inner.LevelChanged -= OnLevel;
        var current = Interlocked.Exchange(ref session, null);
        if (current is not null) await current.DisposeAsync();
    }

    public async Task AbortAsync()
    {
        try { await inner.StopAsync(CancellationToken.None).ConfigureAwait(false); }
        finally { await AbortSessionAsync().ConfigureAwait(false); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await inner.DisposeAsync(); }
        finally { await AbortSessionAsync().ConfigureAwait(false); }
    }
}
