using NAudio.CoreAudioApi;
using VoiceTyper.Core.Audio;

namespace VoiceTyper.Windows.Audio;

internal sealed record RenderMuteState(string Id, bool Muted);

internal interface IRenderEndpointMuteApi
{
    IReadOnlyList<RenderMuteState> Snapshot();
    void SetMuted(string id, bool muted);
}

public sealed class SystemOutputMuteService : ISystemOutputMuteService
{
    private readonly IRenderEndpointMuteApi api;
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<RenderMuteState>? saved;

    public SystemOutputMuteService() : this(new CoreAudioMuteApi()) { }
    internal SystemOutputMuteService(IRenderEndpointMuteApi api) => this.api = api;

    public async Task MuteAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (saved is not null) return;
            var snapshot = await Task.Run(api.Snapshot, cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var endpoint in snapshot) api.SetMuted(endpoint.Id, true);
                saved = snapshot;
            }
            catch
            {
                foreach (var endpoint in snapshot) try { api.SetMuted(endpoint.Id, endpoint.Muted); } catch { }
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public async Task RestoreAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var snapshot = saved;
            saved = null;
            if (snapshot is null) return;
            await Task.Run(() => { foreach (var endpoint in snapshot) try { api.SetMuted(endpoint.Id, endpoint.Muted); } catch { } }).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private sealed class CoreAudioMuteApi : IRenderEndpointMuteApi
    {
        public IReadOnlyList<RenderMuteState> Snapshot()
        {
            using var enumerator = new MMDeviceEnumerator();
            var result = new List<RenderMuteState>();
            foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                using (endpoint) result.Add(new(endpoint.ID, endpoint.AudioEndpointVolume.Mute));
            return result;
        }

        public void SetMuted(string id, bool muted)
        {
            using var enumerator = new MMDeviceEnumerator();
            using var endpoint = enumerator.GetDevice(id);
            endpoint.AudioEndpointVolume.Mute = muted;
        }
    }
}
