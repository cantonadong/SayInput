using System.Windows;
using System.Windows.Threading;
using VoiceTyper.App;
using VoiceTyper.App.Overlay;
using VoiceTyper.App.Tray;
using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Overlay;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class WindowLifecycleTests
{
    [Fact]
    public void Minimized_settings_are_hidden_to_tray() => Assert.True(MainWindow.ShouldHideToTray(WindowState.Minimized));

    [Fact]
    public void Overlay_is_not_constructed_until_first_show()
    {
        var creations = 0;
        var lazy = new LazyRecordingOverlay(() => { creations++; return new Overlay(); });
        lazy.ShowPartial = false;
        Assert.Equal(0, creations);
        lazy.Show(Guid.NewGuid());
        Assert.Equal(1, creations);
    }

    [Fact]
    public async Task Overlay_created_from_a_hotkey_thread_is_marshaled_to_the_sta_dispatcher()
    {
        Dispatcher? dispatcher = null;
        LazyRecordingOverlay? lazy = null;
        var ready = new ManualResetEventSlim();
        var createdOnSta = false;
        var ui = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            lazy = new LazyRecordingOverlay(() =>
            {
                createdOnSta = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA;
                return new Overlay();
            }, dispatcher);
            ready.Set();
            Dispatcher.Run();
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();

        Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            await Task.Run(() => lazy!.Show(Guid.NewGuid()));
            Assert.True(createdOnSta);
        }
        finally
        {
            dispatcher!.InvokeShutdown();
            Assert.True(ui.Join(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public void Sprite_frames_follow_the_visible_pixel_center_instead_of_assuming_even_spacing()
    {
        using var sheet = new System.Drawing.Bitmap(100, 20);
        using (var graphics = System.Drawing.Graphics.FromImage(sheet))
        using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White))
        {
            graphics.FillRectangle(brush, 1, 0, 10, 20);
            graphics.FillRectangle(brush, 77, 0, 18, 20);
        }

        var frames = SpriteFrameDetector.Find(sheet, 2);

        Assert.Equal(0, frames[0].X);
        Assert.Equal(76, frames[1].X);
    }

    [Fact]
    public void Queued_overlay_callbacks_are_rejected_after_the_window_closes()
    {
        var lifetime = new OverlayCallbackLifetime();
        Assert.True(lifetime.IsOpen);
        lifetime.Close();
        Assert.False(lifetime.IsOpen);
    }

    private sealed class Overlay : IRecordingOverlay
    {
        public bool ShowPartial { get; set; }
        public void Show(Guid sessionId) { }
        public void UpdatePartial(Guid sessionId, string text) { }
        public void UpdateLevel(Guid sessionId, AudioLevel level) { }
        public void Hide(Guid sessionId) { }
        public void ShowError(Guid sessionId, string text) { }
    }
}
