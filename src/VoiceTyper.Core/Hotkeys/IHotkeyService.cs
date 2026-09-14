namespace VoiceTyper.Core.Hotkeys;

public interface IHotkeyService : IDisposable
{
    event EventHandler? Pressed;
    event EventHandler? Released;
    void Start();
    void Stop();
}
