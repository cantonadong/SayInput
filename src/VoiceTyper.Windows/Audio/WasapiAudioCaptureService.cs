using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using VoiceTyper.Core.Audio;

namespace VoiceTyper.Windows.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService
{
    private readonly SemaphoreSlim lifecycle = new(1);
    private WasapiCapture? capture;
    private MMDevice? device;
    private TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool disposed;
    public event EventHandler<AudioChunk>? AudioAvailable;
    public event EventHandler<AudioLevel>? LevelChanged;

    public Task<IReadOnlyList<AudioInputDevice>> GetDevicesAsync(CancellationToken cancellationToken) => Task.Run<IReadOnlyList<AudioInputDevice>>(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try { using var current = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console); defaultId = current.ID; }
        catch (System.Runtime.InteropServices.COMException) { }
        var result = new List<AudioInputDevice>();
        foreach (var item in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            using (item) result.Add(new(item.ID, item.FriendlyName, item.ID == defaultId));
        return result;
    }, cancellationToken);

    public async Task StartAsync(string? deviceId, CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (capture is not null) throw new InvalidOperationException("Microphone already recording.");
            await Task.Run(() =>
            {
                using var enumerator = new MMDeviceEnumerator();
                device = string.IsNullOrWhiteSpace(deviceId)
                    ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console) : enumerator.GetDevice(deviceId);
                capture = new WasapiCapture(device, true, 20) { WaveFormat = new WaveFormat(16000, 16, 1) };
                stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
                capture.DataAvailable += OnData;
                capture.RecordingStopped += (_, e) =>
                {
                    if (e.Exception is null) stopped.TrySetResult(); else stopped.TrySetException(e.Exception);
                };
                capture.StartRecording();
            }, cancellationToken).ConfigureAwait(false);
        }
        catch { ReleaseDevice(); throw; }
        finally { lifecycle.Release(); }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        // A small owned PCM block stays valid after the native callback returns.
        var pcm = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
        AudioAvailable?.Invoke(this, new(pcm, DateTimeOffset.UtcNow));
        LevelChanged?.Invoke(this, Measure(pcm));
    }

    internal static AudioLevel Measure(ReadOnlySpan<byte> pcm)
    {
        double sum = 0;
        float peak = 0;
        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(pcm[i..]) / 32768f;
            sum += value * value;
            peak = Math.Max(peak, Math.Abs(value));
        }
        return new(pcm.Length < 2 ? 0 : (float)Math.Sqrt(sum / (pcm.Length / 2)), peak);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (capture is null) return;
            capture.StopRecording();
            // Stop is a resource barrier: no callbacks survive the return, even on cancellation.
            try { await stopped.Task.ConfigureAwait(false); }
            finally { ReleaseDevice(); }
        }
        finally { lifecycle.Release(); }
    }

    private void ReleaseDevice()
    {
        capture?.Dispose(); capture = null;
        device?.Dispose(); device = null;
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
