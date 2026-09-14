namespace VoiceTyper.Core.Audio;

/// <summary>Normalized RMS and peak in the range 0..1.</summary>
public sealed record AudioLevel(float Rms, float Peak);
