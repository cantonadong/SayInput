namespace VoiceTyper.Core.Input;

public sealed record TargetWindow(nint Handle, int ProcessId, string ProcessName, string Title);
