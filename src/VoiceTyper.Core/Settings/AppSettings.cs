namespace VoiceTyper.Core.Settings;

public enum RecordingTriggerMode
{
    Toggle,
    Hold
}

public enum RecordingTriggerEdge
{
    Pressed,
    Released
}

public static class RecordingTriggerPolicy
{
    public static bool ShouldToggle(RecordingTriggerMode mode, RecordingTriggerEdge edge) =>
        edge == RecordingTriggerEdge.Pressed || mode == RecordingTriggerMode.Hold;
}

/// <summary>Persistable non-secret preferences only.</summary>
public sealed record AppSettings(
    bool Enabled = true,
    bool StartWithWindows = true,
    bool ShowPartial = true,
    string? MicrophoneDeviceId = null,
    bool EnableAudioWarmup = true,
    bool EnableDeviceWarmup = false,
    RecordingTriggerMode RecordingTriggerMode = RecordingTriggerMode.Toggle);
