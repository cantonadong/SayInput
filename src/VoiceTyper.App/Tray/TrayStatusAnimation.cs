using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using VoiceTyper.Core.Dictation;

namespace VoiceTyper.App.Tray;

public enum TrayActivity { Idle, Recording, Processing }

public static class TrayActivityMapper
{
    public static TrayActivity From(DictationState state) => state switch
    {
        DictationState.Starting or DictationState.Recording => TrayActivity.Recording,
        DictationState.Finalizing or DictationState.Injecting => TrayActivity.Processing,
        _ => TrayActivity.Idle
    };
}

public static class SpriteFrameDetector
{
    public static IReadOnlyList<Int32Rect> Find(System.Drawing.Bitmap sheet, int count)
    {
        var frames = new List<Int32Rect>(count);
        for (var index = 0; index < count; index++)
        {
            var cellLeft = (int)Math.Floor(index * sheet.Width / (double)count);
            var cellRight = (int)Math.Ceiling((index + 1) * sheet.Width / (double)count) - 1;
            var visibleLeft = cellRight;
            var visibleRight = cellLeft;
            for (var x = cellLeft; x <= cellRight; x++)
                for (var y = 0; y < sheet.Height; y++)
                    if (sheet.GetPixel(x, y).A > 8)
                    {
                        visibleLeft = Math.Min(visibleLeft, x);
                        visibleRight = Math.Max(visibleRight, x);
                    }
            var center = (visibleLeft + visibleRight) / 2d;
            var left = (int)Math.Round(center - sheet.Height / 2d, MidpointRounding.AwayFromZero);
            frames.Add(new(Math.Clamp(left, 0, sheet.Width - sheet.Height), 0, sheet.Height, sheet.Height));
        }
        return frames;
    }
}

internal sealed class TrayStatusAnimation : IDisposable
{
    private readonly Action<System.Drawing.Icon> display;
    private readonly System.Drawing.Icon idle;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<TrayActivity, IReadOnlyList<System.Drawing.Icon>> cache = [];
    private IReadOnlyList<System.Drawing.Icon> frames = [];
    private int frame;

    public TrayStatusAnimation(System.Drawing.Icon idle, Action<System.Drawing.Icon> display)
    {
        this.idle = idle;
        this.display = display;
        timer = new(TimeSpan.FromMilliseconds(110), DispatcherPriority.Background, (_, _) => Advance(), Dispatcher.CurrentDispatcher);
    }

    public void Set(TrayActivity activity)
    {
        timer.Stop();
        frame = 0;
        if (activity == TrayActivity.Idle) { frames = []; display(idle); return; }
        if (!cache.TryGetValue(activity, out frames!))
        {
            frames = Load(activity == TrayActivity.Recording ? "rec.png" : "process.png");
            cache[activity] = frames;
        }
        if (frames.Count == 0) { display(idle); return; }
        display(frames[0]);
        frame = 1;
        timer.Start();
    }

    private IReadOnlyList<System.Drawing.Icon> Load(string fileName)
    {
        var result = new List<System.Drawing.Icon>(8);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "resources", "status", fileName);
            using var sheet = new System.Drawing.Bitmap(path);
            foreach (var source in SpriteFrameDetector.Find(sheet, 8))
            {
                using var bitmap = new System.Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(sheet, new System.Drawing.Rectangle(0, 0, 32, 32),
                        new System.Drawing.Rectangle(source.X, source.Y, source.Width, source.Height), System.Drawing.GraphicsUnit.Pixel);
                }
                var handle = bitmap.GetHicon();
                try { using var borrowed = System.Drawing.Icon.FromHandle(handle); result.Add((System.Drawing.Icon)borrowed.Clone()); }
                finally { DestroyIcon(handle); }
            }
        }
        catch { foreach (var icon in result) icon.Dispose(); result.Clear(); }
        return result;
    }

    private void Advance()
    {
        if (frames.Count == 0) return;
        display(frames[frame++ % frames.Count]);
    }

    public void Dispose()
    {
        timer.Stop();
        foreach (var group in cache.Values)
            foreach (var icon in group) icon.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
