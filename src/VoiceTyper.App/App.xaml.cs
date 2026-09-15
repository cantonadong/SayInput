using System.Windows;
using VoiceTyper.App.Bootstrap;
using VoiceTyper.App.Settings;
using VoiceTyper.App.Tray;
using VoiceTyper.Windows.Audio;

namespace VoiceTyper.App;

public partial class App : System.Windows.Application
{
    private Mutex? instance;
    private DictationRuntime? runtime;
    private TrayIconService? tray;
    private MainWindow? settingsWindow;
    private readonly ConfigurationService configuration = new();
    private readonly CancellationTokenSource lifetime = new();
    private bool exiting;
    private Task warmupTask = Task.CompletedTask;
    private Task toggleTask = Task.CompletedTask;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true, @"Local\VoiceTyper.Desktop", out var first);
        if (!first) { System.Windows.MessageBox.Show("VoiceTyper 已在运行，请从系统托盘打开设置。", "VoiceTyper"); Shutdown(); return; }
        try
        {
            await configuration.LoadAsync();
            runtime = new(configuration);
            tray = new(ShowSettings, () => { if (toggleTask.IsCompleted && !exiting) toggleTask = ToggleAsync(); }, () => _ = ExitAsync());
            runtime.Notice += (message, text) => Dispatcher.BeginInvoke(() => { tray?.Notify(message); settingsWindow?.ShowNotice(message, text); });
            runtime.Coordinator.StateChanged += (_, state) => Dispatcher.BeginInvoke(() => tray?.SetActivity(TrayActivityMapper.From(state)));
            runtime.Apply();
            tray.SetEnabled(configuration.Preferences.Enabled);
            if (configuration.CredentialsNeedReentry)
                tray.Notify("当前 Windows 用户无法解密已有凭据，请在设置中重新填写并保存。");
            if (!e.Args.Contains("--background") || !configuration.HasCredentials) ShowSettings();
            warmupTask = WarmupAsync();
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show("启动失败：无法读取配置或初始化热键。请检查本地配置和系统权限。", "VoiceTyper");
            await ExitAsync();
        }
    }
    private async Task WarmupAsync()
    {
        try { await Task.Delay(1500, lifetime.Token); if (configuration.Preferences.EnableAudioWarmup) await new AudioWarmupService(configuration.Preferences.EnableDeviceWarmup).WarmupAsync(lifetime.Token); }
        catch (OperationCanceledException) { }
    }
    private void ShowSettings()
    {
        if (exiting || runtime is null) return;
        if (settingsWindow is null)
        {
            settingsWindow = new(configuration, runtime, () => tray?.SetEnabled(configuration.Preferences.Enabled), () => _ = ExitAsync());
            MainWindow = settingsWindow;
            settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        settingsWindow.Show(); settingsWindow.WindowState = WindowState.Normal; settingsWindow.Activate();
    }
    private async Task ToggleAsync()
    {
        if (runtime is null || exiting) return;
        try
        {
            await runtime.SuspendAsync(true);
            await configuration.ToggleEnabledAsync();
            runtime.Apply(); tray?.SetEnabled(configuration.Preferences.Enabled); settingsWindow?.RefreshPreferences();
        }
        catch (Exception) { tray?.Notify("无法保存启用状态，请检查配置目录权限。"); }
        finally { await runtime.SuspendAsync(false); }
    }
    private async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; lifetime.Cancel();
        try
        {
            await ShutdownSequence.DrainConfigurationAsync(toggleTask,
                () => settingsWindow?.WaitForSaveAsync() ?? Task.CompletedTask,
                configuration.StopAndDrainAsync);
        }
        catch (Exception) { System.Diagnostics.Trace.TraceWarning("Configuration write failed during shutdown."); }
        try
        {
            if (settingsWindow is not null) await settingsWindow.StopTestsAsync();
            await warmupTask;
        }
        catch (Exception) { System.Diagnostics.Trace.TraceWarning("Settings test or warmup cleanup failed."); }
        try { if (runtime is not null) await runtime.DisposeAsync(); }
        catch (Exception) { System.Diagnostics.Trace.TraceWarning("Dictation cleanup failed."); }
        finally { tray?.Dispose(); tray = null; settingsWindow?.Close(); Shutdown(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); instance?.Dispose(); lifetime.Dispose(); base.OnExit(e);
    }
}
