using System.ComponentModel;
using VoiceTyper.Core.Input;

namespace VoiceTyper.Windows.Input;

public sealed class WindowsForegroundWindowService : IForegroundWindowService
{
    private readonly IForegroundWindowApi api;

    public WindowsForegroundWindowService() : this(new Win32ForegroundWindowApi()) { }
    internal WindowsForegroundWindowService(IForegroundWindowApi api) => this.api = api;

    public TargetWindow? Capture()
    {
        var window = api.GetForegroundWindow();
        if (window == 0) return null;
        var processId = api.GetProcessId(window);
        if (processId <= 0 || processId == Environment.ProcessId) return null;
        try
        {
            var target = new TargetWindow(window, processId, api.GetProcessName(processId), api.GetTitle(window));
            return IsValid(target) ? target : null;
        }
        catch (Win32Exception) { return null; }
    }

    public bool IsValid(TargetWindow target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Handle != 0 && target.ProcessId > 0 && target.ProcessId != Environment.ProcessId
            && api.IsWindow(target.Handle) && api.GetProcessId(target.Handle) == target.ProcessId;
    }

    public Task<bool> EnsureForegroundAsync(TargetWindow target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Focus restoration belongs to the later injection task; fail closed when focus has moved.
        return Task.FromResult(IsValid(target) && api.GetForegroundWindow() == target.Handle);
    }
}
