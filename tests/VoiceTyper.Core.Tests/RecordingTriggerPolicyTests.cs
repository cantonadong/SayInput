using VoiceTyper.Core.Settings;
using Xunit;

namespace VoiceTyper.Core.Tests;

public sealed class RecordingTriggerPolicyTests
{
    [Theory]
    [InlineData(RecordingTriggerMode.Toggle, RecordingTriggerEdge.Pressed, true)]
    [InlineData(RecordingTriggerMode.Toggle, RecordingTriggerEdge.Released, false)]
    [InlineData(RecordingTriggerMode.Hold, RecordingTriggerEdge.Pressed, true)]
    [InlineData(RecordingTriggerMode.Hold, RecordingTriggerEdge.Released, true)]
    public void Selects_edges_for_each_mode(
        RecordingTriggerMode mode, RecordingTriggerEdge edge, bool expected)
    {
        Assert.Equal(expected, RecordingTriggerPolicy.ShouldToggle(mode, edge));
    }
}
