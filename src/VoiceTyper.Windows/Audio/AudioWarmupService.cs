using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceTyper.Core.Audio;

namespace VoiceTyper.Windows.Audio;

public sealed class AudioWarmupService(bool openDevice = false) : IAudioWarmupService
{
    public Task WarmupAsync(CancellationToken cancellationToken) => Task.Run(async () =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            using var capture = new WasapiCapture(device, true, 20) { WaveFormat = new WaveFormat(16000, 16, 1) };
            // WASAPI's shared-mode AutoConvertPcm provides the native resampler.
            if (openDevice)
            {
                capture.StartRecording();
                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                capture.StopRecording();
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { System.Diagnostics.Trace.TraceWarning("Audio warm-up unavailable."); }
    }, cancellationToken);
}
