using System.Diagnostics;
using VoiceTyper.Windows.Audio;
using Xunit;
using Xunit.Abstractions;

namespace VoiceTyper.Windows.Tests.Audio;

public sealed class HardwareAudioTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Hardware")]
    public async Task Fifty_capture_cycles_release_the_device_and_produce_pcm()
    {
        await using var audio = new WasapiAudioCaptureService();
        var devices = await audio.GetDevicesAsync(default);
        Assert.NotEmpty(devices);
        for (var i = 0; i < 50; i++)
        {
            var first = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callbacks = 0;
            void Frame(object? sender, Core.Audio.AudioChunk chunk) { Interlocked.Increment(ref callbacks); first.TrySetResult(chunk.Pcm16.Length); }
            audio.AudioAvailable += Frame;
            var timer = Stopwatch.StartNew();
            try
            {
                await audio.StartAsync(null, default);
                var bytes = await first.Task.WaitAsync(TimeSpan.FromSeconds(3));
                output.WriteLine($"Cycle {i + 1}: first frame {timer.Elapsed.TotalMilliseconds:F1} ms, bytes {bytes}");
                Assert.True(bytes > 0 && bytes % 2 == 0);
            }
            finally { await audio.StopAsync(default); audio.AudioAvailable -= Frame; }
            var stoppedCount = callbacks;
            await Task.Delay(20);
            Assert.Equal(stoppedCount, callbacks);
        }
    }
}
