using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using VoiceTyper.App.Bootstrap;
using VoiceTyper.App.Settings;
using VoiceTyper.Core.Audio;
using VoiceTyper.Core.Speech;
using VoiceTyper.Volcengine;
using VoiceTyper.Windows.Audio;
using VoiceTyper.App.Branding;

namespace VoiceTyper.App;

public partial class MainWindow : Window
{
    private readonly ConfigurationService configuration;
    private readonly DictationRuntime runtime;
    private readonly Action saved;
    private readonly Action exit;
    private CancellationTokenSource? testCancellation;
    private Task testTask = Task.CompletedTask;
    private Task saveTask = Task.CompletedTask;
    private bool closing;
    private long lastLevel;
    public MainWindow(ConfigurationService configuration, DictationRuntime runtime, Action saved, Action exit)
    {
        this.configuration = configuration; this.runtime = runtime; this.saved = saved; this.exit = exit;
        InitializeComponent(); RefreshPreferences();
        Icon = AppIcon.Load();
        AppIdBox.Text = configuration.Provider.AppId; TokenBox.Password = configuration.Provider.AccessToken;
        ApiKeyBox.Password = configuration.Provider.ApiKey; ResourceBox.Text = configuration.Provider.ResourceId;
        Loaded += async (_, _) => await RefreshDevicesAsync();
        if (runtime.LastError is not null) ShowNotice(runtime.LastError, runtime.RecoveryText);
        Metrics.Text = runtime.MetricsText;
        runtime.MetricsUpdated += UpdateMetrics;
        Closed += (_, _) => runtime.MetricsUpdated -= UpdateMetrics;
        StateChanged += (_, _) => { if (ShouldHideToTray(WindowState)) Hide(); };
    }
    internal static bool ShouldHideToTray(WindowState state) => state == WindowState.Minimized;
    public void RefreshPreferences()
    {
        EnabledOption.IsChecked = configuration.Preferences.Enabled;
        StartupOption.IsChecked = configuration.Preferences.StartWithWindows;
        PartialOption.IsChecked = configuration.Preferences.ShowPartial;
    }
    private void UpdateMetrics() => Dispatcher.BeginInvoke(() => Metrics.Text = runtime.MetricsText);
    public void ShowNotice(string message, string? text)
    {
        Status.Text = message;
        if (text is null) return;
        RecoveryText.Text = text; RecoveryText.Visibility = Visibility.Visible;
    }
    private VolcengineOptions ReadProvider() => new()
    {
        AppId = AppIdBox.Text.Trim(), AccessToken = TokenBox.Password.Trim(), ApiKey = ApiKeyBox.Password.Trim(), ResourceId = ResourceBox.Text.Trim()
    };
    private async void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (configuration.IsStopping || !saveTask.IsCompleted) return;
        saveTask = SaveAsync();
        await saveTask;
    }
    public Task WaitForSaveAsync() => saveTask;
    private async Task SaveAsync()
    {
        SaveButton.IsEnabled = false;
        try
        {
            await runtime.SuspendAsync(true);
            var preferences = configuration.Preferences with
            {
                Enabled = EnabledOption.IsChecked == true, StartWithWindows = StartupOption.IsChecked == true,
                ShowPartial = PartialOption.IsChecked == true,
                MicrophoneDeviceId = string.IsNullOrEmpty(Devices.SelectedValue as string) ? null : Devices.SelectedValue as string
            };
            await configuration.SaveAsync(preferences, ReadProvider());
            runtime.Apply(); saved(); Status.Text = "已保存。切换到记事本，按一下右 Alt 开始，再按一下结束。";
        }
        catch (Exception) { Status.Text = "保存失败，请检查凭据格式、配置目录和开机启动权限。"; }
        finally { await runtime.SuspendAsync(false); SaveButton.IsEnabled = true; }
    }
    private async void RefreshDevicesClicked(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();
    private async Task RefreshDevicesAsync()
    {
        try
        {
            await using var audio = new WasapiAudioCaptureService();
            var devices = await audio.GetDevicesAsync(default);
            Devices.ItemsSource = new[] { new AudioInputDevice("", "系统默认麦克风", true) }.Concat(devices).ToArray();
            Devices.SelectedValue = configuration.Preferences.MicrophoneDeviceId ?? "";
            if (Devices.SelectedIndex < 0) { Devices.SelectedIndex = 0; Status.Text = "已保存的麦克风不可用，请选择设备并保存。"; }
        }
        catch (Exception) { Status.Text = "未找到可用麦克风，请检查设备和 Windows 麦克风权限。"; }
    }
    private async void LevelClicked(object sender, RoutedEventArgs e)
    {
        if (configuration.IsStopping || !testTask.IsCompleted || !saveTask.IsCompleted) return;
        var device = Devices.SelectedValue as string;
        testTask = RunTestAsync(async token =>
        {
            await using var audio = new WasapiAudioCaptureService();
            audio.LevelChanged += (_, level) =>
            {
                if (Stopwatch.GetElapsedTime(Interlocked.Read(ref lastLevel)).TotalMilliseconds < 50) return;
                Interlocked.Exchange(ref lastLevel, Stopwatch.GetTimestamp());
                Dispatcher.BeginInvoke(() => { if (!token.IsCancellationRequested) LevelMeter.Value = level.Peak; });
            };
            await audio.StartAsync(string.IsNullOrEmpty(device) ? null : device, token);
            Status.Text = "请说话，正在测试音量；音频不会上传。";
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            await audio.StopAsync(default); Status.Text = "音量测试完成，麦克风已释放。";
        });
        await testTask;
    }
    private async void ConnectionClicked(object sender, RoutedEventArgs e)
    {
        if (configuration.IsStopping || !testTask.IsCompleted || !saveTask.IsCompleted) return;
        var provider = ReadProvider();
        testTask = RunTestAsync(async token =>
        {
            Status.Text = "正在验证云端连接…";
            await Task.Run(async () =>
            {
                await using var speech = new VolcengineStreamingRecognizer(provider);
                await speech.StartAsync(new SpeechRecognitionOptions(Guid.NewGuid()), token);
                await speech.AbortAsync(default);
            }, token);
            Status.Text = "连接成功，云端已接受配置。请保存设置后测试实际语音输入。";
        });
        await testTask;
    }
    private async Task RunTestAsync(Func<CancellationToken, Task> run)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        testCancellation = cancellation;
        ConnectionButton.IsEnabled = SaveButton.IsEnabled = LevelButton.IsEnabled = false;
        try { await runtime.SuspendAsync(true); await run(cancellation.Token); }
        catch (OperationCanceledException) { Status.Text = "测试已停止或超时。"; }
        catch (Exception error) { Status.Text = ConnectionFailureMessage(error); }
        finally
        {
            LevelMeter.Value = 0; testCancellation = null;
            await runtime.SuspendAsync(false);
            ConnectionButton.IsEnabled = SaveButton.IsEnabled = LevelButton.IsEnabled = true;
        }
    }
    public async Task StopTestsAsync() { testCancellation?.Cancel(); await testTask; }
    internal static string ConnectionFailureMessage(Exception error) => error.Message switch
    {
        "Speech credentials were rejected." => "连接失败：API Key 或 App ID / Access Token 被拒绝，请重新复制凭据。",
        "Speech resource is not enabled for these credentials." => "连接失败：当前凭据未开通所选 Resource ID，请在豆包语音控制台开通对应服务。",
        "Speech service rate limit was reached." => "连接失败：调用频率或并发额度已用尽，请稍后重试或检查配额。",
        "Speech service is temporarily unavailable." => "连接失败：语音服务暂时不可用，请稍后重试。",
        "Network unavailable or speech service connection failed." => "连接失败：网络连接被阻止，请检查 SimpleWall；放行后完全退出并重启本程序。",
        _ => "测试失败，请检查麦克风、网络、凭据和 Resource ID。"
    };
    private void ExitClicked(object sender, RoutedEventArgs e) => exit();
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!closing && !testTask.IsCompleted)
        {
            e.Cancel = true; await StopTestsAsync(); closing = true; Close();
        }
        base.OnClosing(e);
    }
}
