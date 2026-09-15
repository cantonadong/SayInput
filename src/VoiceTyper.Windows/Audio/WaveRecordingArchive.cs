using NAudio.Wave;

namespace VoiceTyper.Windows.Audio;

public interface IWaveRecordingSession : IAsyncDisposable
{
    void Write(ReadOnlySpan<byte> pcm);
    Task CompleteAsync();
}

public sealed class WaveRecordingArchive
{
    private readonly string directory;
    public WaveRecordingArchive(string directory) => this.directory = Path.GetFullPath(directory);

    public IWaveRecordingSession BeginSession()
    {
        Directory.CreateDirectory(directory);
        var stem = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8];
        return new Session(this, Path.Combine(directory, stem + ".tmp"), Path.Combine(directory, stem + ".wav"));
    }

    public Task CleanupAsync() => Task.Run(() =>
    {
        Directory.CreateDirectory(directory);
        foreach (var temporary in Directory.EnumerateFiles(directory, "*.tmp")) try { File.Delete(temporary); } catch { }
        foreach (var stale in Directory.EnumerateFiles(directory, "*.wav").OrderByDescending(Path.GetFileName).Skip(20))
            try { File.Delete(stale); } catch { }
    });

    private sealed class Session(WaveRecordingArchive owner, string temporary, string completed) : IWaveRecordingSession
    {
        private WaveFileWriter? writer = new(temporary, new WaveFormat(16000, 16, 1));
        private long bytes;

        public void Write(ReadOnlySpan<byte> pcm)
        {
            var current = writer ?? throw new ObjectDisposedException(nameof(Session));
            current.Write(pcm);
            bytes += pcm.Length;
        }

        public async Task CompleteAsync()
        {
            var current = Interlocked.Exchange(ref writer, null);
            if (current is null) return;
            current.Dispose();
            if (bytes == 0) File.Delete(temporary);
            else { File.Move(temporary, completed); await owner.CleanupAsync().ConfigureAwait(false); }
        }

        public ValueTask DisposeAsync()
        {
            var current = Interlocked.Exchange(ref writer, null);
            if (current is not null)
            {
                current.Dispose();
                try { File.Delete(temporary); } catch { }
            }
            return ValueTask.CompletedTask;
        }
    }
}
