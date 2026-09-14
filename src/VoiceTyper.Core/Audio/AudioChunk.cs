namespace VoiceTyper.Core.Audio;

/// <summary>16 kHz, signed PCM16 little-endian, mono. The producer must keep memory valid until consumption completes.</summary>
public sealed record AudioChunk(ReadOnlyMemory<byte> Pcm16, DateTimeOffset CapturedAt);
