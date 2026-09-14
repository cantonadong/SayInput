using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VoiceTyper.Windows.Input;

internal sealed class Win32ForegroundWindowApi : IForegroundWindowApi
{
    public nint GetForegroundWindow() => NativeGetForegroundWindow();

    public int GetProcessId(nint window) =>
        GetWindowThreadProcessId(window, out var processId) != 0 ? checked((int)processId) : 0;

    public bool IsWindow(nint window) => NativeIsWindow(window);

    public string GetProcessName(int processId)
    {
        using var process = OpenProcess(0x1000, false, processId);
        if (process.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var name = new StringBuilder(32768);
        var length = name.Capacity;
        if (!QueryFullProcessImageNameW(process, 0, name, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return Path.GetFileNameWithoutExtension(name.ToString());
    }

    public string GetTitle(nint window)
    {
        // Only external-process windows reach this method: Win32 reads their caption without WM_GETTEXT.
        var title = new StringBuilder(4096);
        GetWindowTextW(window, title, title.Capacity);
        return title.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "GetForegroundWindow")]
    private static extern nint NativeGetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "IsWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeIsWindow(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int count);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, StringBuilder name, ref int length);
}
