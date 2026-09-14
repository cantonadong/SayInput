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
        // ponytail: at most 10 seconds/320 KB of unsent PCM; sustained network stalls abort instead of dropping speech.
        var queue = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(500)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var pendingBytes = 0;
        Exception? overflow = null;
        void OnAudio(object? sender, AudioChunk chunk)
        {
            metrics.Mark(sessionId, PerformanceEvent.FirstAudioFrame);
            if (chunk.Pcm16.Length % 2 != 0 || Interlocked.Add(ref pendingBytes, chunk.Pcm16.Length) > 320000 || !queue.Writer.TryWrite(chunk))
            {
                overflow = new IOException("音频发送积压，已停止本次识别，请重试。");
                queue.Writer.TryComplete(overflow);
                lifetime.Cancel();
            }
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
