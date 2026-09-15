using VoiceTyper.App.Bootstrap;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class AppShutdownTests
{
    [Fact]
    public async Task Configuration_stops_accepting_writes_only_after_active_ui_operations_finish()
    {
        var activeSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = false;
        var shutdown = ShutdownSequence.DrainConfigurationAsync(Task.CompletedTask, () => activeSave.Task,
            () => { stopped = true; return Task.CompletedTask; });

        await Task.Delay(20);
        Assert.False(stopped);
        activeSave.SetResult();
        await shutdown;
        Assert.True(stopped);
    }
}
