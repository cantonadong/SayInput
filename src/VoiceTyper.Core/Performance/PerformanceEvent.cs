namespace VoiceTyper.Core.Performance;

public enum PerformanceEvent
{
    HotkeyPressed,
    TargetCaptured,
    AudioStartRequested,
    FirstAudioFrame,
    OverlayShown,
    AsrConnectStarted,
    AsrConnected,
    FirstAudioSent,
    PreRollFlushed,
    FirstPartial,
    HotkeyReleased,
    FinalReceived,
    InjectionStarted,
    InjectionCompleted,
    SessionCompleted
}
