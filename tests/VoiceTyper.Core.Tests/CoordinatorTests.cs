using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Dictation;
using VoiceTyper.Core.Input;
using VoiceTyper.Core.Overlay;
using VoiceTyper.Core.Performance;
using Xunit;

namespace VoiceTyper.Core.Tests;

public sealed class CoordinatorTests
{
    [Theory]
    [InlineData(false, "你好", true, 1)]
    [InlineData(false, "", true, 0)]
    [InlineData(true, "你好", true, 0)]
    [InlineData(false, "你好", false, 0)]
    public async Task One_session_injects_only_final_and_recovers(bool fail, string text, bool valid, int count)
    {
        var audio = new PipelineTests.FakeAudio();
        var speech = new PipelineTests.FakeSpeech { Delay = 50, Fail = fail, Text = text };
        var windows = new Windows { Valid = valid };
        var overlay = new Overlay();
        var injection = new Injection();
        await using var coordinator = new DictationCoordinator(() => audio, () => speech, windows, overlay, injection, new PerformanceMetrics());
        Assert.True(coordinator.TryStart());
        Assert.False(coordinator.TryStart());
        coordinator.Release(); // May arrive before StartAsync finishes.
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.Equal(count, injection.Text.Count);
        Assert.DoesNotContain("stale", overlay.Partials);
        Assert.DoesNotContain("partial", injection.Text);
        if (count == 1) Assert.Equal("你好", injection.Text.Single());
        Assert.True(audio.Stopped);
    }

    [Fact]
    public async Task Cancel_during_connection_cleans_up_and_allows_next_session()
    {
        var audio = new PipelineTests.FakeAudio();
        await using var coordinator = new DictationCoordinator(() => audio,
            () => new PipelineTests.FakeSpeech { Delay = 1000 }, new Windows(), new Overlay(), new Injection(), new PerformanceMetrics());
        Assert.True(coordinator.TryStart());
        await coordinator.CancelAsync();
        Assert.Equal(DictationState.Idle, coordinator.State);
        Assert.True(coordinator.TryStart());
        await coordinator.CancelAsync();
    }
    private sealed class Windows : IForegroundWindowService
    {
        public bool Valid = true;
        public TargetWindow? Capture() => new(1, 2, "test", "test");
        public bool IsValid(TargetWindow t) => Valid;
        public Task<bool> EnsureForegroundAsync(TargetWindow t, CancellationToken ct) => Task.FromResult(Valid);
    }
    private sealed class Injection : ITextInjectionService
    {
        public List<string> Text = [];
        public Task<TextInjectionResult> InjectAsync(TargetWindow t, string text, CancellationToken ct)
        { Text.Add(text); return Task.FromResult(new TextInjectionResult(true, false)); }
    }
    private sealed class Overlay : IRecordingOverlay
    {
        public List<string> Partials = [];
        public void Show(Guid id) { }
        public void UpdatePartial(Guid id, string text) => Partials.Add(text);
        public void UpdateLevel(Guid id, AudioLevel level) { }
        public void Hide(Guid id) { }
        public void ShowError(Guid id, string text) { }
    }
}
