using Microsoft.Win32;

namespace VoiceTyper.App.Settings;

public interface IStartupRegistration
{
    void Apply(bool enabled);
}

internal sealed class WindowsStartupRegistration : IStartupRegistration
{
    public void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
            key.SetValue("SayInput", $"\"{Environment.ProcessPath}\" --background");
        else
            key.DeleteValue("SayInput", false);
        key.DeleteValue("VoiceTyper", false);
    }
}
