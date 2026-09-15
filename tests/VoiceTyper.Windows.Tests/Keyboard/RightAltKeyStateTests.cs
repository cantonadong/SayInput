using VoiceTyper.Windows.Keyboard;
using Xunit;

namespace VoiceTyper.Windows.Tests.Keyboard;

public sealed class RightAltKeyStateTests
{
    [Theory]
    [InlineData(0x100u, 0x101u)]
    [InlineData(0x104u, 0x105u)]
    public void Held_key_with_repeats_emits_one_press_and_one_release(uint down, uint up)
    {
        var state = new RightAltKeyState();
        Assert.Equal(new KeyDecision(true, KeyEdge.Pressed), state.Process(0xA5, down, 0));
        for (var i = 0; i < 150; i++)
            Assert.Equal(new KeyDecision(true, KeyEdge.None), state.Process(0xA5, down, 0));
        Assert.Equal(new KeyDecision(true, KeyEdge.Released), state.Process(0xA5, up, 0));
        Assert.Equal(new KeyDecision(true, KeyEdge.None), state.Process(0xA5, up, 0));
    }

    [Theory]
    [InlineData(0xA4u, 0x104u, 0u)]
    [InlineData(0x41u, 0x100u, 0u)]
    [InlineData(0xA5u, 0x100u, 0x10u)]
    [InlineData(0xA5u, 0x100u, 0x02u)]
    [InlineData(0xA5u, 0x200u, 0u)]
    public void Unrelated_or_injected_events_do_not_change_pressed_state(uint key, uint message, uint flags)
    {
        var state = new RightAltKeyState();
        Assert.Equal(default, state.Process(key, message, flags));
        Assert.Equal(new KeyDecision(true, KeyEdge.None), state.Process(0xA5, 0x101, 0));
        Assert.Equal(new KeyDecision(true, KeyEdge.Pressed), state.Process(0xA5, 0x100, 0));
        Assert.Equal(default, state.Process(key, message, flags));
        Assert.Equal(new KeyDecision(true, KeyEdge.Released), state.Process(0xA5, 0x101, 0));
    }

    [Fact]
    public void Reset_clears_held_key_for_next_start()
    {
        var state = new RightAltKeyState();
        state.Process(0xA5, 0x100, 0);
        state.Reset();
        Assert.Equal(new KeyDecision(true, KeyEdge.None), state.Process(0xA5, 0x101, 0));
        Assert.Equal(new KeyDecision(true, KeyEdge.Pressed), state.Process(0xA5, 0x104, 0));
    }

    [Fact]
    public void Escape_is_only_suppressed_and_emitted_while_cancellation_is_enabled()
    {
        var state = new RightAltKeyState();
        Assert.Equal(default, state.Process(0x1B, 0x100, 0, false));
        Assert.Equal(new KeyDecision(true, KeyEdge.Cancelled), state.Process(0x1B, 0x100, 0, true));
        Assert.Equal(new KeyDecision(true, KeyEdge.None), state.Process(0x1B, 0x101, 0, true));
    }
}
