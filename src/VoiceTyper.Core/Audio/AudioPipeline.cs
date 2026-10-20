using System.Threading.Channels;
using VoiceTyper.Core.Performance;
using VoiceTyper.Core.Speech;

namespace VoiceTyper.Core.Audio;

public sealed class AudioPipeline(IAudioCaptureService audio, IStreamingSpeechRecognizer speech,
    IPerformanceMetrics metrics, Guid sessionId)
{
    public async Task RunAsync(string? deviceId, Task released, Action audioReady, CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Recording duration is limited by the coordinator; retain all captured speech while uploads catch up.
        var queue = Channel.CreateUnbounded<AudioChunk>(new UnboundedChannelOptions
        { SingleReader = true, SingleWriter = true });
        var pendingBytes = 0;
        Exception? overflow = null;
        void OnAudio(object? sender, AudioChunk chunk)
        {
            metrics.Mark(sessionId, PerformanceEvent.FirstAudioFrame);
            if (chunk.Pcm16.Length % 2 != 0)
            {
                overflow = new IOException("麦克风音频数据格式异常，请重试。");
                queue.Writer.TryComplete(overflow);
                lifetime.Cancel();
                return;
            }
            Interlocked.Add(ref pendingBytes, chunk.Pcm16.Length);
            if (!queue.Writer.TryWrite(chunk)) Interlocked.Add(ref pendingBytes, -chunk.Pcm16.Length);
        }
        audio.AudioAvailable += OnAudio;
        Task? stopper = null;
        try
        {
            metrics.Mark(sessionId, PerformanceEvent.AudioStartRequested);
            await audio.StartAsync(deviceId, lifetime.Token).ConfigureAwait(false);
            audioReady();
            stopper = StopWhenReleasedAsync();
            metrics.Mark(sessionId, PerformanceEvent.AsrConnectStarted);
            await speech.StartAsync(new(sessionId), lifetime.Token).ConfigureAwait(false);
            metrics.Mark(sessionId, PerformanceEvent.AsrConnected);
            await foreach (var chunk in queue.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                Interlocked.Add(ref pendingBytes, -chunk.Pcm16.Length);
                await speech.SendAudioAsync(chunk.Pcm16, lifetime.Token).ConfigureAwait(false);
                metrics.Mark(sessionId, PerformanceEvent.FirstAudioSent);
                if (Volatile.Read(ref pendingBytes) == 0) metrics.Mark(sessionId, PerformanceEvent.PreRollFlushed);
            }
            await stopper.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (overflow is not null) { throw overflow; }
        finally
        {
            lifetime.Cancel();
            try
            {
                if (stopper is not null)
                    try { await stopper.ConfigureAwait(false); } catch (OperationCanceledException) { }
                if (cancellationToken.IsCancellationRequested && audio is IAbortableAudioCaptureService abortable)
                    await abortable.AbortAsync().ConfigureAwait(false);
                else
                    await audio.StopAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally { audio.AudioAvailable -= OnAudio; queue.Writer.TryComplete(); }
        }

        async Task StopWhenReleasedAsync()
        {
            try
            {
                await released.WaitAsync(lifetime.Token).ConfigureAwait(false);
                await audio.StopAsync(CancellationToken.None).ConfigureAwait(false);
                queue.Writer.TryComplete();
            }
            catch (Exception error) { queue.Writer.TryComplete(error); throw; }
        }
    }
}
