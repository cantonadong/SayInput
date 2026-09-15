using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Dictation;
using VoiceTyper.Core.Performance;
using VoiceTyper.Core.Speech;
using Xunit;

namespace VoiceTyper.Core.Tests;

public sealed class PipelineTests
{
    [Theory]
    [InlineData(320002, 1)] // Byte budget.
    [InlineData(2, 501)] // Chunk budget.
    [InlineData(3, 1)] // Incomplete PCM16 sample.
    public async Task Invalid_or_overflowing_audio_aborts_without_sending_and_next_run_is_clean(int size, int chunks)
    {
        var audio = new FakeAudio();
        var speech = new FakeSpeech { Delay = 1000 };
        var pipeline = new AudioPipeline(audio, speech, new PerformanceMetrics(), Guid.NewGuid());
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await Assert.ThrowsAsync<IOException>(() => pipeline.RunAsync(null, released.Task, () =>
        {
            for (var i = 0; i < chunks; i++) audio.EmitBytes(new byte[size]);
        }, default).WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(audio.Stopped);
        Assert.Empty(speech.Audio);

        audio.Stopped = false;
        speech.Delay = 0;
        await pipeline.RunAsync(null, released.Task, () => { audio.Emit(7); released.SetResult(); }, default)
            .WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(new byte[] { 7, 0, 3, 0 }, speech.Audio);
    }

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

    [Fact]
    public async Task User_cancellation_aborts_capture_so_the_recording_is_discarded()
    {
        var audio = new AbortableAudio();
        var cancellation = new CancellationTokenSource();
        var pipeline = new AudioPipeline(audio, new FakeSpeech(), new PerformanceMetrics(), Guid.NewGuid());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.RunAsync(null, Task.Delay(Timeout.Infinite),
            cancellation.Cancel, cancellation.Token));

        Assert.True(audio.Aborted);
        Assert.False(audio.Stopped);
    }

    internal class FakeAudio : IAudioCaptureService
    {
        public event EventHandler<AudioChunk>? AudioAvailable;
        public event EventHandler<AudioLevel>? LevelChanged { add { } remove { } }
        public bool Stopped;
        public void EmitBytes(byte[] pcm) => AudioAvailable?.Invoke(this, new(pcm, DateTimeOffset.UtcNow));
        public void Emit(byte value) => AudioAvailable?.Invoke(this, new(new byte[] { value, 0 }, DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AudioInputDevice>>([]);
        public Task StartAsync(string? id, CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) { if (!Stopped) { Emit(3); Stopped = true; } return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class AbortableAudio : FakeAudio, IAbortableAudioCaptureService
    {
        public bool Aborted;
        public Task AbortAsync() { Aborted = true; return Task.CompletedTask; }
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
