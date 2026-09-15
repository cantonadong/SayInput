using VoiceTyper.App.Sound;
using VoiceTyper.Core.Dictation;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class SoundFeedbackTests
{
    [Fact]
    public void Session_plays_each_transition_once_and_success_after_completion()
    {
        var player = new Player();
        var feedback = new DictationSoundFeedback(player);
        var id = Guid.NewGuid();

        feedback.StateChanged(id, DictationState.Recording);
        feedback.StateChanged(id, DictationState.Recording);
        feedback.StateChanged(id, DictationState.Finalizing);
        feedback.StateChanged(id, DictationState.Finalizing);
        feedback.Finished(null);

        Assert.Equal([SoundCue.Start, SoundCue.Stop, SoundCue.Success], player.Cues);
    }

    [Fact]
    public void Failed_session_and_immediate_rejection_play_error()
    {
        var player = new Player();
        var feedback = new DictationSoundFeedback(player);

        feedback.Finished("failed");
        feedback.Error();

        Assert.Equal([SoundCue.Error, SoundCue.Error], player.Cues);
    }

    [Fact]
    public void Missing_or_invalid_wave_files_are_disabled_without_fallback()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(directory.Path, "resources", "sounds"));
        File.WriteAllText(Path.Combine(directory.Path, "resources", "sounds", "start.wav"), "not a wave");

        using var player = new WaveSoundCuePlayer(directory.Path);
        player.Play(SoundCue.Start);
        player.Play(SoundCue.Error);

        Assert.Empty(player.AvailableCues);
    }

    private sealed class Player : ISoundCuePlayer
    {
        public List<SoundCue> Cues { get; } = [];
        public void Play(SoundCue cue) => Cues.Add(cue);
        public Task PlayAsync(SoundCue cue) { Play(cue); return Task.CompletedTask; }
    }
}
