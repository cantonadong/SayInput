namespace VoiceTyper.Core.Dictation;

public sealed record DictationSession(Guid SessionId, Input.TargetWindow Target, DateTimeOffset StartedAt);
