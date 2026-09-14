using VoiceTyper.Core.Performance;
using Xunit;

namespace VoiceTyper.Core.Tests;

public sealed class PerformanceTests
{
    [Fact]
    public void Milestones_keep_first_value_and_snapshots_survive_clear()
    {
        var metrics = new PerformanceMetrics();
        var id = Guid.NewGuid();
        metrics.Mark(id, PerformanceEvent.HotkeyPressed);
        var snapshot = metrics.Snapshot(id);
        metrics.Mark(id, PerformanceEvent.HotkeyPressed);
        metrics.Mark(id, PerformanceEvent.FirstAudioFrame);
        Assert.Single(snapshot.Milestones);
        Assert.Equal(snapshot.Milestones[PerformanceEvent.HotkeyPressed], metrics.Snapshot(id).Milestones[PerformanceEvent.HotkeyPressed]);
        metrics.Clear(id);
        Assert.Single(snapshot.Milestones);
        Assert.Empty(metrics.Snapshot(id).Milestones);
    }
}
