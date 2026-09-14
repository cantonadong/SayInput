using System.Runtime.InteropServices;
using VoiceTyper.Windows.Input;
using Xunit;

namespace VoiceTyper.Windows.Tests.Input;

public sealed class Win32ForegroundWindowApiTests
{
    [Fact]
    public void Native_adapter_reads_and_releases_a_real_hidden_window()
    {
        var window = CreateWindowExW(0, "STATIC", "VoiceTyper 本机测试", 0x80000000,
            0, 0, 32, 32, 0, 0, 0, 0);
        Assert.NotEqual(0, window);
        var api = new Win32ForegroundWindowApi();
        try
        {
            Assert.True(api.IsWindow(window));
            Assert.Equal(Environment.ProcessId, api.GetProcessId(window));
            Assert.Equal("VoiceTyper 本机测试", api.GetTitle(window));
            Assert.Equal(Path.GetFileNameWithoutExtension(Environment.ProcessPath!), api.GetProcessName(Environment.ProcessId));
        }
        finally { Assert.True(DestroyWindow(window)); }
        Assert.False(api.IsWindow(window));
        Assert.Equal(0, api.GetProcessId(window));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);
}
