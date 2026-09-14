using VoiceTyper.Windows.Audio;
using Xunit;

namespace VoiceTyper.Windows.Tests.Audio;

public sealed class AudioTests
{
    [Fact]
    public void Pcm_level_uses_signed_samples_and_silence()
    {
        var level = WasapiAudioCaptureService.Measure(new byte[] { 0, 0, 0, 128, 0, 64, 0, 192 });
        Assert.Equal(1f, level.Peak);
        Assert.InRange(level.Rms, 0.6123f, 0.6124f);
        Assert.Equal(0, WasapiAudioCaptureService.Measure(new byte[10]).Peak);
    }

    [Fact]
    public void Ring_wraps_in_order_and_reset_discards_previous_session()
    {
        var ring = new PcmRingBuffer(6);
        ring.Write(new byte[] { 1, 2, 3, 4 });
        var first = new byte[2];
        Assert.Equal(2, ring.Read(first));
        Assert.Equal(new byte[] { 1, 2 }, first);
        ring.Write(new byte[] { 5, 6, 7, 8, 9, 10 });
        var rest = new byte[6];
        Assert.Equal(6, ring.Read(rest));
        Assert.Equal(new byte[] { 5, 6, 7, 8, 9, 10 }, rest);
        ring.Write(new byte[] { 11, 12 });
        ring.Reset();
        Assert.Equal(0, ring.Read(rest));
    }

    [Fact]
    public async Task Cancelled_warmup_does_not_open_devices() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AudioWarmupService().WarmupAsync(new CancellationToken(true)));
}
