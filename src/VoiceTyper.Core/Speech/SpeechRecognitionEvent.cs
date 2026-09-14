namespace VoiceTyper.Core.Speech;

/// <summary>Session-tagged update; partial text is for the overlay only.</summary>
public sealed record SpeechRecognitionEvent(Guid SessionId, string Text, bool IsFinal);
