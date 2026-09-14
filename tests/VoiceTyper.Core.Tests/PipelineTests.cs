using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Dictation;
using VoiceTyper.Core.Performance;
using VoiceTyper.Core.Speech;
using Xunit;

namespace VoiceTyper.Core.Tests;

public sealed class PipelineTests
{
    [Theory]
    [InlineData(0)] [InlineData(100)] [InlineData(300)] [InlineData(800)]
    public async Task Delayed_connection_keeps_every_initial_sample_and_flushes_stop_tail(int delay)
    {
        var audio = new FakeAudio();
        var asr = new FakeSpeech { Delay = delay };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pipeline = new AudioPipeline(audio, asr, new PerformanceMetrics(), Guid.NewGuid());
        var run = pipeline.RunAsync(null, release.Task, () => { audio.Emit(1); audio.Emit(2); release.SetResult(); }, default);
        await run.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(new byte[] { 1, 0, 2, 0, 3, 0 }, asr.Audio.ToArray());
        Assert.True(audio.Stopped);
    }

    internal sealed class FakeAudio : IAudioCaptureService
    {
        public event EventHandler<AudioChunk>? AudioAvailable;
        public event EventHandler<AudioLevel>? LevelChanged { add { } remove { } }
        public bool Stopped;
        public void Emit(byte value) => AudioAvailable?.Invoke(this, new(new byte[] { value, 0 }, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AudioInputDevice>>([]);
        public Task StartAsync(string? id, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { if (!Stopped) { Emit(3); Stopped = true; } return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    internal sealed class FakeSpeech : IStreamingSpeechRecognizer
    {
        public int Delay;
        public bool Fail = false;
        public string Text = "你好";
        public readonly List<byte> Audio = [];
        public event EventHandler<SpeechRecognitionEvent>? RecognitionUpdated;
        public async Task StartAsync(SpeechRecognitionOptions o, CancellationToken ct)
        {
            await Task.Delay(Delay, ct);
            if (Fail) throw new IOException("ASR unavailable");
            RecognitionUpdated?.Invoke(this, new(Guid.NewGuid(), "stale", false));
            RecognitionUpdated?.Invoke(this, new(o.SessionId, "partial", false));
        }
        public ValueTask SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct) { Audio.AddRange(pcm.ToArray()); return ValueTask.CompletedTask; }
        public Task<DictationResult> CompleteAsync(CancellationToken ct) => Task.FromResult(new DictationResult(Text));
        public Task AbortAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
