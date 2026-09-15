namespace VoiceTyper.Windows.Keyboard;

internal enum KeyEdge { None, Pressed, Released, Cancelled }
internal readonly record struct KeyDecision(bool Suppress, KeyEdge Edge);

internal sealed class RightAltKeyState
{
    private bool held;
    private bool escapeHeld;

    public KeyDecision Process(uint virtualKey, uint message, uint flags, bool cancelEnabled = false)
    {
        if ((flags & 0x12) != 0)
            return default;
        if (message is not (0x100 or 0x101 or 0x104 or 0x105))
            return default;

        if (virtualKey == 0x1B)
        {
            var escapeDown = message is 0x100 or 0x104;
            if (!cancelEnabled) { if (!escapeDown) escapeHeld = false; return default; }
            var escapeEdge = escapeDown == escapeHeld ? KeyEdge.None : escapeDown ? KeyEdge.Cancelled : KeyEdge.None;
            escapeHeld = escapeDown;
            return new KeyDecision(true, escapeEdge);
        }
        if (virtualKey != 0xA5) return default;

        var down = message is 0x100 or 0x104;
        var edge = down == held ? KeyEdge.None : down ? KeyEdge.Pressed : KeyEdge.Released;
        held = down;
        return new KeyDecision(true, edge);
    }

    public void Reset() { held = false; escapeHeld = false; }
}
