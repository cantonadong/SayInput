using System.Diagnostics;
using System.Windows;
using VoiceTyper.Windows.Keyboard;
using VoiceTyper.Windows.Input;

namespace VoiceTyper.App;

public partial class MainWindow : Window
{
    private readonly RightAltHotkeyService hotkey = new();
    private readonly WindowsForegroundWindowService windows = new();
    private int generation, pressed, released;
    private long pressedAt;
    private bool running;

    public MainWindow()
    {
        InitializeComponent();
        hotkey.Pressed += (_, _) => QueueEdge(true);
        hotkey.Released += (_, _) => QueueEdge(false);
    }

    private void QueueEdge(bool down)
    {
        var version = Volatile.Read(ref generation);
        var timestamp = Stopwatch.GetTimestamp();
        var target = down ? windows.Capture() : null;
        Dispatcher.BeginInvoke(() =>
        {
            if (!running || version != generation) return;
            if (down)
            {
                pressed++;
                pressedAt = timestamp;
                Target.Text = target is null ? "未捕获到可用外部窗口；请切到记事本测试。"
                    : $"目标：{target.ProcessName} · PID {target.ProcessId}\n窗口：{target.Title}\nHWND：0x{target.Handle:X}";
                Status.Text = "已按下，继续按住……";
            }
            else
            {
                released++;
                Status.Text = $"已松开，按住时长 {Stopwatch.GetElapsedTime(pressedAt, timestamp).TotalSeconds:F2} 秒。";
            }
            Counts.Text = $"按下 {pressed} 次 · 松开 {released} 次";
        });
    }

    private void StartClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Interlocked.Increment(ref generation);
            hotkey.Start();
            running = true;
            pressed = released = 0;
            Counts.Text = "按下 0 次 · 松开 0 次";
            Status.Text = "已启用。可切换到其他应用按住右 Alt 测试。";
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }
        catch (Exception error) { Status.Text = $"启动失败：{error.Message}"; }
    }

    private void StopClicked(object sender, RoutedEventArgs e)
    {
        running = false;
        Interlocked.Increment(ref generation);
        try
        {
            hotkey.Stop();
            Status.Text = "已停止，右 Alt 恢复原有用途。";
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
        catch (Exception error) { Status.Text = $"停止失败：{error.Message}"; }
    }

    protected override void OnClosed(EventArgs e)
    {
        running = false;
        Interlocked.Increment(ref generation);
        try { hotkey.Dispose(); }
        catch (Exception) { Trace.TraceError("Hotkey cleanup failed during window shutdown."); }
        base.OnClosed(e);
    }
}
