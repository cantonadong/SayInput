namespace VoiceTyper.Core.Input;

public sealed record TextInjectionResult(bool Succeeded, bool CopiedToClipboard, string? ErrorMessage = null);
