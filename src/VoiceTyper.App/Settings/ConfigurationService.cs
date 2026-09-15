using System.Text.Json;
using VoiceTyper.Core.Settings;
using VoiceTyper.Volcengine;
using VoiceTyper.Windows.Security;
using VoiceTyper.Windows.Settings;
using VoiceTyper.App.Storage;
using System.Security.Cryptography;

namespace VoiceTyper.App.Settings;

public sealed class ConfigurationService
{
    private readonly JsonSettingsStore settings;
    private readonly DpapiCredentialStore credentials;
    private readonly IStartupRegistration startup;
    private readonly object writes = new();
    private Task pendingWrites = Task.CompletedTask;
    private bool stopping;
    public bool IsStopping { get { lock (writes) return stopping; } }
    public AppSettings Preferences { get; private set; } = new();
    public VolcengineOptions Provider { get; private set; } = new();
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Provider.ApiKey) ||
        (!string.IsNullOrWhiteSpace(Provider.AppId) && !string.IsNullOrWhiteSpace(Provider.AccessToken));
    public PortablePaths Paths { get; }
    public bool CredentialsNeedReentry { get; private set; }

    public ConfigurationService(PortablePaths? paths = null, IStartupRegistration? startup = null)
    {
        paths ??= new PortablePaths();
        Paths = paths;
        this.startup = startup ?? new WindowsStartupRegistration();
        paths.EnsureAndMigrate();
        settings = new JsonSettingsStore(paths.SettingsPath);
        credentials = new DpapiCredentialStore(paths.CredentialDirectory);
    }

    public async Task LoadAsync()
    {
        Preferences = await settings.LoadAsync(default);
        startup.Apply(Preferences.StartWithWindows);
        string? secret;
        try { secret = await credentials.ReadAsync("volcengine-options-v1", default); }
        catch (CryptographicException) { CredentialsNeedReentry = true; secret = null; }
        if (secret is not null) Provider = NormalizeProvider(JsonSerializer.Deserialize<VolcengineOptions>(secret) ?? new());
    }

    public Task SaveAsync(AppSettings preferences, VolcengineOptions provider) =>
        Enqueue(() => SaveCoreAsync(preferences, provider));

    public Task ToggleEnabledAsync() => Enqueue(() =>
        SaveCoreAsync(Preferences with { Enabled = !Preferences.Enabled }, Provider));

    private Task Enqueue(Func<Task> write)
    {
        lock (writes)
        {
            if (stopping) return Task.FromException(new InvalidOperationException("Application is shutting down."));
            return pendingWrites = RunAfterAsync(pendingWrites, write);
        }
    }
    private static async Task RunAfterAsync(Task previous, Func<Task> write)
    {
        // A failed write must not prevent later writes or orderly shutdown.
        try { await previous.ConfigureAwait(false); } catch (Exception) { }
        await write().ConfigureAwait(false);
    }
    public Task StopAndDrainAsync()
    {
        lock (writes) { stopping = true; return pendingWrites; }
    }
    private async Task SaveCoreAsync(AppSettings preferences, VolcengineOptions provider)
    {
        provider = NormalizeProvider(provider);
        if (new[] { provider.AppId, provider.AccessToken, provider.ApiKey, provider.ResourceId }.Any(v => v.Any(char.IsControl)) ||
            string.IsNullOrWhiteSpace(provider.ResourceId))
            throw new InvalidOperationException("请填写有效的凭据和 Resource ID。");
        await credentials.WriteAsync("volcengine-options-v1", JsonSerializer.Serialize(provider), default);
        startup.Apply(preferences.StartWithWindows);
        await settings.SaveAsync(preferences, default);
        Provider = provider;
        Preferences = preferences;
    }

    internal static VolcengineOptions NormalizeProvider(VolcengineOptions provider) =>
        provider with { Endpoint = new VolcengineOptions().Endpoint };
}
