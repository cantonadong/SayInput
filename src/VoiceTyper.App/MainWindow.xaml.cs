using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using VoiceTyper.App.Bootstrap;
using VoiceTyper.App.Settings;
using VoiceTyper.Core.Speech;
using VoiceTyper.Volcengine;
using VoiceTyper.App.Branding;
using VoiceTyper.Core.Settings;
using VoiceTyper.Core.Audio;
using VoiceTyper.Windows.Audio;

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
    private bool controlsReady;
    private bool credentialsEditing;
    private long lastLevel;
    public MainWindow(ConfigurationService configuration, DictationRuntime runtime, Action saved, Action exit)
    {
        this.configuration = configuration; this.runtime = runtime; this.saved = saved; this.exit = exit;
        InitializeComponent();
        RefreshPreferences();
        Icon = AppIcon.Load(); BrandIcon.Source = AppIcon.LoadBrandImage();
        ApiKeyBox.Password = configuration.Provider.ApiKey; ResourceBox.Text = configuration.Provider.ResourceId;
        SetCredentialEditing(!configuration.HasCredentials);
        SaveButton.IsEnabled = false;
        Loaded += async (_, _) => { await RefreshDevicesAsync(); controlsReady = true; };
        if (runtime.LastError is not null) ShowNotice(runtime.LastError, runtime.RecoveryText);
        StateChanged += (_, _) => { if (ShouldHideToTray(WindowState)) Hide(); };
    }
    internal static bool ShouldHideToTray(WindowState state) => state == WindowState.Minimized;
    internal static string CredentialButtonText(bool editing) => editing ? "保存" : "编辑";
    public void RefreshPreferences()
    {
        StartupOption.IsChecked = configuration.Preferences.StartWithWindows;
        PartialOption.IsChecked = configuration.Preferences.ShowPartial;
        HoldMode.IsChecked = configuration.Preferences.RecordingTriggerMode == RecordingTriggerMode.Hold;
        ToggleMode.IsChecked = configuration.Preferences.RecordingTriggerMode == RecordingTriggerMode.Toggle;
    }
    public void ShowNotice(string message, string? text)
    {
        Status.Text = message;
        if (text is null) return;
        RecoveryText.Text = text; RecoveryText.Visibility = Visibility.Visible;
    }
    private VolcengineOptions ReadProvider() => new()
    {
        AppId = configuration.Provider.AppId,
        AccessToken = configuration.Provider.AccessToken,
        ApiKey = ApiKeyBox.Password.Trim(),
        ResourceId = ResourceBox.Text.Trim()
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
        CredentialButton.IsEnabled = false;
        SetPreferencesEnabled(false);
        var succeeded = false;
        try
        {
            await runtime.SuspendAsync(true);
            var preferences = configuration.Preferences with
            {
                StartWithWindows = StartupOption.IsChecked == true,
                ShowPartial = PartialOption.IsChecked == true,
                RecordingTriggerMode = HoldMode.IsChecked == true ? RecordingTriggerMode.Hold : RecordingTriggerMode.Toggle,
                MicrophoneDeviceId = string.IsNullOrEmpty(Devices.SelectedValue as string) ? null : Devices.SelectedValue as string
            };
            await configuration.SaveAsync(preferences, ReadProvider());
            runtime.Apply(); saved(); SetCredentialEditing(false);
            Status.Text = "已保存";
            succeeded = true;
        }
        catch (Exception) { Status.Text = "保存失败，请检查凭据格式、配置目录和开机启动权限。"; }
        finally
        {
            await runtime.SuspendAsync(false);
            CredentialButton.IsEnabled = true;
            SaveButton.IsEnabled = !succeeded;
            SetPreferencesEnabled(true);
        }
    }
    private async void CredentialClicked(object sender, RoutedEventArgs e)
    {
        if (!credentialsEditing) { SetCredentialEditing(true); return; }
        if (configuration.IsStopping || !saveTask.IsCompleted) return;
        saveTask = SaveAsync();
        await saveTask;
    }
    private void PreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (controlsReady && testTask.IsCompleted && saveTask.IsCompleted) SaveButton.IsEnabled = true;
    }
    private void SetCredentialEditing(bool editing)
    {
        credentialsEditing = editing;
        ProviderBox.IsEnabled = editing;
        ApiKeyBox.IsEnabled = ResourceBox.IsEnabled = editing;
        CredentialButton.Content = CredentialButtonText(editing);
    }
    private void SetPreferencesEnabled(bool enabled)
    {
        StartupOption.IsEnabled = PartialOption.IsEnabled = enabled;
        HoldMode.IsEnabled = ToggleMode.IsEnabled = enabled;
        Devices.IsEnabled = enabled;
    }
    private async Task RefreshDevicesAsync()
    {
        try
        {
            await using var audio = new WasapiAudioCaptureService();
            var devices = await audio.GetDevicesAsync(default);
            Devices.ItemsSource = new[] { new AudioInputDevice("", "跟随系统", true) }.Concat(devices).ToArray();
            Devices.SelectedValue = configuration.Preferences.MicrophoneDeviceId ?? "";
            if (Devices.SelectedIndex < 0)
            {
                Devices.SelectedIndex = 0;
                Status.Text = "已保存的麦克风不可用，当前改为跟随系统；保存后生效。";
            }
        }
        catch (Exception) { Status.Text = "未找到可用麦克风，请检查设备和 Windows 麦克风权限。"; }
    }
    private async void LevelClicked(object sender, RoutedEventArgs e)
    {
        if (!testTask.IsCompleted)
        {
            testCancellation?.Cancel();
            await testTask;
            return;
        }
        if (configuration.IsStopping || !saveTask.IsCompleted) return;
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
            Status.Text = "正在测试麦克风音量，音频不会上传。";
            await Task.Delay(TimeSpan.FromSeconds(10), token);
            await audio.StopAsync(default);
            Status.Text = "麦克风测试完成。";
        }, allowLevelStop: true);
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
    private async Task RunTestAsync(Func<CancellationToken, Task> run, bool allowLevelStop = false)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        testCancellation = cancellation;
        var wasEditing = ApiKeyBox.IsEnabled;
        var wasSaveEnabled = SaveButton.IsEnabled;
        ConnectionButton.IsEnabled = SaveButton.IsEnabled = CredentialButton.IsEnabled = false;
        SetPreferencesEnabled(false);
        LevelButton.IsEnabled = allowLevelStop;
        LevelButton.Content = allowLevelStop ? "结束" : "测试";
        try { await runtime.SuspendAsync(true); await run(cancellation.Token); }
        catch (OperationCanceledException) { Status.Text = "测试已停止或超时。"; }
        catch (Exception error) { Status.Text = ConnectionFailureMessage(error); }
        finally
        {
            LevelMeter.Value = 0; testCancellation = null;
            await runtime.SuspendAsync(false);
            ConnectionButton.IsEnabled = LevelButton.IsEnabled = true;
            LevelButton.Content = "测试";
            SetCredentialEditing(wasEditing);
            CredentialButton.IsEnabled = true;
            SaveButton.IsEnabled = wasSaveEnabled;
            SetPreferencesEnabled(true);
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
    private void MinimizeClicked(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (!closing && !testTask.IsCompleted)
        {
            e.Cancel = true; await StopTestsAsync(); closing = true; Close();
        }
        base.OnClosing(e);
    }
}
