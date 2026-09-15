using VoiceTyper.Core.Dictation;

namespace VoiceTyper.App.Sound;

public enum SoundCue { Start, Stop, Success, Error }

public interface ISoundCuePlayer
{
    void Play(SoundCue cue);
    Task PlayAsync(SoundCue cue);
}

public sealed class DictationSoundFeedback(ISoundCuePlayer player)
{
    private readonly object gate = new();
    private Guid started;
    private Guid stopped;

    public void StateChanged(Guid session, DictationState state)
    {
        SoundCue? cue = null;
        lock (gate)
        {
            if (state == DictationState.Recording && started != session)
            {
                started = session;
                cue = SoundCue.Start;
            }
            else if (state == DictationState.Finalizing && stopped != session)
            {
                stopped = session;
                cue = SoundCue.Stop;
            }
        }
        if (cue.HasValue) player.Play(cue.Value);
    }

    public void Finished(string? error) => player.Play(error is null ? SoundCue.Success : SoundCue.Error);
    public void Error() => player.Play(SoundCue.Error);
}
