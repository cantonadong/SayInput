using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Overlay;

namespace VoiceTyper.App.Overlay;

public partial class RecordingOverlay : Window, IRecordingOverlay
{
    private readonly object gate = new();
    private Guid session;
    private string partial = "";
    private float level;
    private long lastFrame;
    private bool rendering;
    public bool ShowPartial { get; set; } = true;

    public RecordingOverlay()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtrW(hwnd, -20, GetWindowLongPtrW(hwnd, -20) | 0x08000000 | 0x20 | 0x80);
        };
        Closed += (_, _) => StopRendering();
    }
    public void Show(Guid sessionId)
    {
        lock (gate) { session = sessionId; partial = ""; level = 0; }
        Dispatcher.BeginInvoke(() =>
        {
            lock (gate) if (session != sessionId) return;
            Left = SystemParameters.WorkArea.Left + (SystemParameters.WorkArea.Width - Width) / 2;
            Top = SystemParameters.WorkArea.Bottom - Height - 40;
            Transcript.Text = "请说话…";
            Status.Text = "正在聆听 · 松开右 Alt 完成";
            Wave.Reset();
            Show();
            if (!rendering) { CompositionTarget.Rendering += Render; rendering = true; }
        });
    }
    public void UpdatePartial(Guid id, string text) { lock (gate) if (session == id) partial = text; }
    public void UpdateLevel(Guid id, AudioLevel value) { lock (gate) if (session == id) level = value.Peak; }
    public void Finalizing(Guid id) => Dispatcher.BeginInvoke(() =>
    {
        lock (gate) if (session != id) return;
        Status.Text = "正在完成识别…";
    });
    public void ShowError(Guid id, string message) => Dispatcher.BeginInvoke(async () =>
    {
        lock (gate) if (session != id) return;
        StopRendering();
        Status.Text = "本次输入未完成";
        Transcript.Text = message;
        Show();
        await Task.Delay(4000);
        Hide(id);
    });
    public void Hide(Guid id) => Dispatcher.BeginInvoke(() =>
    {
        lock (gate) { if (session != id) return; session = Guid.Empty; partial = ""; level = 0; }
        StopRendering();
        Hide();
    });
    private void Render(object? sender, EventArgs e)
    {
        if (Stopwatch.GetElapsedTime(lastFrame).TotalMilliseconds < 33.4) return;
        lastFrame = Stopwatch.GetTimestamp();
        lock (gate)
        {
            if (ShowPartial && partial.Length > 0 && Transcript.Text != partial) Transcript.Text = partial;
            Wave.Push(level);
            level *= 0.8f;
        }
    }
    private void StopRendering()
    {
        if (!rendering) return;
        CompositionTarget.Rendering -= Render;
        rendering = false;
    }
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] private static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
}

public sealed class Waveform : FrameworkElement
{
    private readonly double[] bars = new double[22];
    private static readonly Brush Color = new SolidColorBrush(System.Windows.Media.Color.FromRgb(94, 222, 202)).GetAsFrozen() as Brush ?? Brushes.Turquoise;
    private double display;
    private int position;
    public void Reset() { Array.Clear(bars); display = 0; position = 0; InvalidateVisual(); }
    public void Push(float incoming)
    {
        display += (Math.Clamp(incoming * 3, 0, 1) - display) * (incoming > display ? 0.65 : 0.22);
        bars[position++ % bars.Length] = display;
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        for (var i = 0; i < bars.Length; i++)
        {
            var height = 3 + bars[(position + i) % bars.Length] * Math.Max(0, ActualHeight - 3);
            dc.DrawRoundedRectangle(Color, null, new Rect(i * ActualWidth / bars.Length, (ActualHeight - height) / 2, 3, height), 1.5, 1.5);
        }
    }
}
