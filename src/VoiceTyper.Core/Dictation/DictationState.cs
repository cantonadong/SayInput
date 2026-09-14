namespace VoiceTyper.Core.Dictation;

public enum DictationState
{
    Idle,
    Starting,
    Recording,
    Finalizing,
    Injecting,
    Failed
}
