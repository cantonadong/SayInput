namespace VoiceTyper.Core.Dictation;

/// <summary>Final recognition output only; empty text means no text to inject.</summary>
public sealed record DictationResult(string Text);
