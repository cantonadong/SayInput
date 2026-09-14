namespace VoiceTyper.Core.Input;

public interface IForegroundWindowService
{
    TargetWindow? Capture();
    bool IsValid(TargetWindow target);
    Task<bool> EnsureForegroundAsync(TargetWindow target, CancellationToken cancellationToken);
}
