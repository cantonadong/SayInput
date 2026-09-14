using VoiceTyper.Windows.Keyboard;
using Xunit;

namespace VoiceTyper.Windows.Tests.Keyboard;

public sealed class RightAltHotkeyServiceTests
{
    [Fact]
    public void Real_hook_can_start_stop_and_restart_without_a_caller_message_loop()
    {
        using var service = new RightAltHotkeyService();
        service.Stop();
        for (var i = 0; i < 10; i++)
        {
            service.Start();
            service.Start();
            service.Stop();
            service.Stop();
        }
    }

    [Fact]
    public void Dispose_releases_running_hook_and_rejects_restart()
    {
        var service = new RightAltHotkeyService();
        service.Start();
        service.Dispose();
        service.Dispose();
        Assert.Throws<ObjectDisposedException>(service.Start);
    }
}
