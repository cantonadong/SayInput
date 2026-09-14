namespace VoiceTyper.Core.Settings;

/// <summary>Persistable non-secret preferences only.</summary>
public sealed record AppSettings(
    bool Enabled = true,
    bool StartWithWindows = false,
    bool ShowPartial = true,
    string? MicrophoneDeviceId = null,
    bool EnableAudioWarmup = true,
    bool EnableDeviceWarmup = false);
