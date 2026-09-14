namespace VoiceTyper.Windows.Input;

internal interface IForegroundWindowApi
{
    nint GetForegroundWindow();
    int GetProcessId(nint window);
    string GetProcessName(int processId);
    string GetTitle(nint window);
    bool IsWindow(nint window);
}
