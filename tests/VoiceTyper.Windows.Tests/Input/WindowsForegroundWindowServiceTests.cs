using System.ComponentModel;
using VoiceTyper.Core.Input;
using VoiceTyper.Windows.Input;
using Xunit;

namespace VoiceTyper.Windows.Tests.Input;

public sealed class WindowsForegroundWindowServiceTests
{
    [Fact]
    public void Capture_keeps_original_window_when_focus_changes_during_metadata_lookup()
    {
        var api = new FakeWindows();
        api.ReadTitle = _ => { api.Foreground = 222; return "原始窗口"; };
        var target = new WindowsForegroundWindowService(api).Capture();
        Assert.Equal(new TargetWindow(111, 42, "notepad", "原始窗口"), target);
    }

    [Theory]
    [InlineData(0, 42)]
    [InlineData(111, 0)]
    public void Missing_window_or_owner_returns_no_target(int handle, int processId)
    {
        var api = new FakeWindows { Foreground = handle, Owner = processId };
        Assert.Null(new WindowsForegroundWindowService(api).Capture());
    }

    [Fact]
    public void Own_process_is_not_an_injection_target()
    {
        var api = new FakeWindows { Owner = Environment.ProcessId };
        Assert.Null(new WindowsForegroundWindowService(api).Capture());
    }

    [Fact]
    public void Window_closed_during_capture_is_rejected()
    {
        var api = new FakeWindows();
        api.ReadTitle = _ => { api.Exists = false; return "closed"; };
        Assert.Null(new WindowsForegroundWindowService(api).Capture());
    }

    [Fact]
    public void Handle_reassigned_during_capture_is_rejected()
    {
        var api = new FakeWindows();
        api.ReadTitle = _ => { api.Owner = 99; return "replacement"; };
        Assert.Null(new WindowsForegroundWindowService(api).Capture());
    }

    [Fact]
    public void Unreadable_process_is_rejected_without_crashing_hook()
    {
        var api = new FakeWindows { ReadName = _ => throw new Win32Exception(5) };
        Assert.Null(new WindowsForegroundWindowService(api).Capture());
    }

    [Fact]
    public void Validation_rejects_a_different_owner_but_allows_title_changes()
    {
        var api = new FakeWindows();
        var service = new WindowsForegroundWindowService(api);
        var target = service.Capture()!;
        api.ReadTitle = _ => "edited document";
        Assert.True(service.IsValid(target));
        api.Owner = 99;
        Assert.False(service.IsValid(target));
        api.Owner = 42;
        api.Exists = false;
        Assert.False(service.IsValid(target));
    }

    [Fact]
    public async Task Foreground_check_only_accepts_the_valid_original_target()
    {
        var api = new FakeWindows();
        var service = new WindowsForegroundWindowService(api);
        var target = service.Capture()!;
        Assert.True(await service.EnsureForegroundAsync(target, default));
        api.Foreground = 222;
        Assert.False(await service.EnsureForegroundAsync(target, default));
        api.Foreground = 111;
        api.Owner = 99;
        Assert.False(await service.EnsureForegroundAsync(target, default));
    }

    [Fact]
    public async Task Cancelled_foreground_check_is_cancelled()
    {
        var service = new WindowsForegroundWindowService(new FakeWindows());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EnsureForegroundAsync(
            new TargetWindow(111, 42, "notepad", "document"), new CancellationToken(true)));
    }

    private sealed class FakeWindows : IForegroundWindowApi
    {
        public nint Foreground = 111;
        public int Owner = 42;
        public bool Exists = true;
        public Func<int, string> ReadName = _ => "notepad";
        public Func<nint, string> ReadTitle = _ => "document";
        public nint GetForegroundWindow() => Foreground;
        public int GetProcessId(nint window) => Owner;
        public string GetProcessName(int processId) => ReadName(processId);
        public string GetTitle(nint window) => ReadTitle(window);
        public bool IsWindow(nint window) => Exists;
        public bool TrySetForeground(nint window) => false;
    }
}
