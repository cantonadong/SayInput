using VoiceTyper.App.Sound;
using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Dictation;

namespace VoiceTyper.App.Bootstrap;

public sealed class ToggleDictationController(
    Func<DictationState> state,
    Func<bool> start,
    Action release,
    ISystemOutputMuteService mute,
    ISoundCuePlayer sounds,
    Func<Task>? cancel = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task ToggleAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            switch (state())
            {
                case DictationState.Idle:
                    await sounds.PlayAsync(SoundCue.Start).ConfigureAwait(false);
                    await mute.MuteAsync(default).ConfigureAwait(false);
                    if (!start()) await mute.RestoreAsync().ConfigureAwait(false);
                    break;
                case DictationState.Starting:
                case DictationState.Recording:
                    release();
                    await mute.RestoreAsync().ConfigureAwait(false);
                    await sounds.PlayAsync(SoundCue.Stop).ConfigureAwait(false);
                    break;
            }
        }
        finally { gate.Release(); }
    }

    public async Task CompleteAsync(DictationCompletion completion)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await mute.RestoreAsync().ConfigureAwait(false);
            if (completion == DictationCompletion.Succeeded) sounds.Play(SoundCue.Success);
            else if (completion == DictationCompletion.Failed) sounds.Play(SoundCue.Error);
        }
        finally { gate.Release(); }
    }

    public async Task CancelAsync()
    {
        Task? completion = null;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (state() is DictationState.Starting or DictationState.Recording)
            {
                completion = cancel?.Invoke();
                await mute.RestoreAsync().ConfigureAwait(false);
            }
        }
        finally { gate.Release(); }
        if (completion is not null) await completion.ConfigureAwait(false);
    }

    public Task StopAsync() => CompleteAsync(DictationCompletion.NoSpeech);
}
