using NAudio.Wave;
using VoiceTyper.Windows.Audio;
using Xunit;

namespace VoiceTyper.Windows.Tests.Audio;

public sealed class WaveRecordingArchiveTests
{
    [Fact]
    public async Task Completed_pcm_becomes_valid_wave_and_empty_session_creates_nothing()
    {
        using var area = new TemporaryDirectory();
        var archive = new WaveRecordingArchive(area.Path);
        await archive.CleanupAsync();
        await using (var empty = archive.BeginSession()) await empty.CompleteAsync();
        await using (var session = archive.BeginSession())
        {
            session.Write(new byte[] { 1, 0, 2, 0 });
            await session.CompleteAsync();
        }

        var file = Assert.Single(Directory.GetFiles(area.Path, "*.wav"));
        using var reader = new WaveFileReader(file);
        Assert.Equal(16000, reader.WaveFormat.SampleRate);
        Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        Assert.Equal(1, reader.WaveFormat.Channels);
        Assert.Equal(4, reader.Length);
    }

    [Fact]
    public async Task Cleanup_retains_only_newest_twenty_completed_files_and_removes_temps()
    {
        using var area = new TemporaryDirectory();
        for (var i = 0; i < 22; i++) File.WriteAllBytes(Path.Combine(area.Path, $"20260101-0000{i:D2}-x.wav"), [0]);
        File.WriteAllText(Path.Combine(area.Path, "orphan.tmp"), "x");

        await new WaveRecordingArchive(area.Path).CleanupAsync();

        Assert.Equal(20, Directory.GetFiles(area.Path, "*.wav").Length);
        Assert.Empty(Directory.GetFiles(area.Path, "*.tmp"));
        Assert.False(File.Exists(Path.Combine(area.Path, "20260101-000000-x.wav")));
    }
}
