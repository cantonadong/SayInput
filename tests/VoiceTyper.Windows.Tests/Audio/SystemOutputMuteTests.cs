using VoiceTyper.Windows.Audio;
using Xunit;

namespace VoiceTyper.Windows.Tests.Audio;

public sealed class SystemOutputMuteTests
{
    [Fact]
    public async Task Restores_each_original_state_and_repeated_restore_is_harmless()
    {
        var api = new MuteApi(("a", false), ("b", true));
        var service = new SystemOutputMuteService(api);

        await service.MuteAsync(default);
        Assert.True(api.State["a"]);
        Assert.True(api.State["b"]);

        await service.RestoreAsync();
        await service.RestoreAsync();
        Assert.False(api.State["a"]);
        Assert.True(api.State["b"]);
    }

    private sealed class MuteApi(params (string Id, bool Muted)[] endpoints) : IRenderEndpointMuteApi
    {
        public Dictionary<string, bool> State { get; } = endpoints.ToDictionary(x => x.Id, x => x.Muted);
        public IReadOnlyList<RenderMuteState> Snapshot() => State.Select(x => new RenderMuteState(x.Key, x.Value)).ToArray();
        public void SetMuted(string id, bool muted) => State[id] = muted;
    }
}
