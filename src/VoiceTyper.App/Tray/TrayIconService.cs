using Forms = System.Windows.Forms;
using System.IO;

namespace VoiceTyper.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly System.Drawing.Icon appIcon;
    private readonly Forms.ToolStripMenuItem enabled;
    private readonly Forms.ContextMenuStrip menu;
    private readonly TrayStatusAnimation animation;
    public TrayIconService(Action showSettings, Action toggleEnabled, Action exit)
    {
        menu = new();
        enabled = new("启用右 Alt 录音", null, (_, _) => toggleEnabled());
        menu.Items.Add(enabled);
        menu.Items.Add("设置", null, (_, _) => showSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        appIcon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "resources", "icon.ico"));
        icon = new() { Icon = appIcon, Text = "SayInput · 右 Alt 录音", ContextMenuStrip = menu, Visible = true };
        animation = new(appIcon, value => icon.Icon = value);
        icon.DoubleClick += (_, _) => showSettings();
    }
    public void SetEnabled(bool value) => enabled.Checked = value;
    public void SetActivity(TrayActivity activity)
    {
        animation.Set(activity);
        icon.Text = activity switch
        {
            TrayActivity.Recording => "SayInput · 正在录音",
            TrayActivity.Processing => "SayInput · 正在转写",
            _ => "SayInput · 右 Alt 录音"
        };
    }
    public void Notify(string message) => icon.ShowBalloonTip(4000, "SayInput", message, Forms.ToolTipIcon.Info);
    public void Dispose() { icon.Visible = false; animation.Dispose(); icon.Dispose(); appIcon.Dispose(); menu.Dispose(); }
}
