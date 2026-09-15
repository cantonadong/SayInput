using VoiceTyper.App.Bootstrap;
using VoiceTyper.App.Sound;
using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Dictation;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class ToggleDictationControllerTests
{
    [Fact]
    public async Task Clicks_order_start_sound_mute_start_then_release_restore_stop_sound()
    {
        var log = new List<string>();
        var state = DictationState.Idle;
        var controller = new ToggleDictationController(() => state, () => { log.Add("start"); state = DictationState.Recording; return true; },
            () => { log.Add("release"); state = DictationState.Finalizing; }, new Mute(log), new Sounds(log));

        await controller.ToggleAsync();
        await controller.ToggleAsync();

        Assert.Equal(["sound:Start", "mute", "start", "release", "restore", "sound:Stop"], log);
    }

    [Fact]
    public async Task Completion_restores_output_and_silences_no_speech()
    {
        var log = new List<string>();
        var controller = new ToggleDictationController(() => DictationState.Idle, () => true, () => { }, new Mute(log), new Sounds(log));

        await controller.CompleteAsync(DictationCompletion.NoSpeech);
        await controller.CompleteAsync(DictationCompletion.Succeeded);
        await controller.CompleteAsync(DictationCompletion.Failed);

        Assert.Equal(["restore", "restore", "sound:Success", "restore", "sound:Error"], log);
    }

    [Fact]
    public async Task Cancel_stops_the_session_and_restores_output_without_a_result_sound()
    {
        var log = new List<string>();
        var state = DictationState.Recording;
        var controller = new ToggleDictationController(() => state, () => true, () => { },
            new Mute(log), new Sounds(log), () => { log.Add("cancel"); state = DictationState.Idle; return Task.CompletedTask; });

        await controller.CancelAsync();

        Assert.Equal(["cancel", "restore"], log);
    }

    private sealed class Mute(List<string> log) : ISystemOutputMuteService
    {
        public Task MuteAsync(CancellationToken cancellationToken) { log.Add("mute"); return Task.CompletedTask; }
        public Task RestoreAsync() { log.Add("restore"); return Task.CompletedTask; }
    }
    private sealed class Sounds(List<string> log) : ISoundCuePlayer
    {
        public void Play(SoundCue cue) => log.Add("sound:" + cue);
        public Task PlayAsync(SoundCue cue) { Play(cue); return Task.CompletedTask; }
    }
}
