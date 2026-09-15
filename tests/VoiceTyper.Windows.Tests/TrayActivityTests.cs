using VoiceTyper.App.Tray;
using VoiceTyper.Core.Dictation;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class TrayActivityTests
{
    [Theory]
    [InlineData(DictationState.Idle, TrayActivity.Idle)]
    [InlineData(DictationState.Starting, TrayActivity.Recording)]
    [InlineData(DictationState.Recording, TrayActivity.Recording)]
    [InlineData(DictationState.Finalizing, TrayActivity.Processing)]
    [InlineData(DictationState.Injecting, TrayActivity.Processing)]
    [InlineData(DictationState.Failed, TrayActivity.Idle)]
    public void Dictation_state_selects_the_expected_tray_activity(DictationState state, TrayActivity expected) =>
        Assert.Equal(expected, TrayActivityMapper.From(state));
}
