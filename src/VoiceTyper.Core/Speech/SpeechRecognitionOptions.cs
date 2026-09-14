namespace VoiceTyper.Core.Speech;

/// <summary>Provider-independent session options. Authentication belongs to the provider and credential store.</summary>
public sealed record SpeechRecognitionOptions(Guid SessionId, string Language = "zh-CN");
