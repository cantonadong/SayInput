using VoiceTyper.App.Settings;
using VoiceTyper.Volcengine;
using VoiceTyper.App.Storage;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class ConfigurationMigrationTests
{
    [Fact]
    public void Saved_legacy_endpoint_is_replaced_by_current_streaming_2_0_endpoint()
    {
        var saved = new VolcengineOptions
        {
            ApiKey = "synthetic-key",
            ResourceId = "volc.seedasr.sauc.duration",
            Endpoint = new("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel")
        };

        var migrated = ConfigurationService.NormalizeProvider(saved);

        Assert.Equal("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel_async", migrated.Endpoint.AbsoluteUri);
        Assert.Equal(saved.ApiKey, migrated.ApiKey);
        Assert.Equal(saved.ResourceId, migrated.ResourceId);
    }

    [Fact]
    public async Task Credential_encrypted_for_another_user_is_treated_as_missing_so_settings_can_open()
    {
        var root = Path.Combine(Path.GetTempPath(), "VoiceTyperTests", Guid.NewGuid().ToString("N"));
        var paths = new PortablePaths(root);
        paths.EnsureAndMigrate();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("volcengine-options-v1"))) + ".bin";
        await File.WriteAllBytesAsync(Path.Combine(paths.CredentialDirectory, name), [1, 2, 3, 4]);

        var configuration = new ConfigurationService(paths);
        await configuration.LoadAsync();

        Assert.True(configuration.CredentialsNeedReentry);
        Assert.False(configuration.HasCredentials);
        Assert.True(File.Exists(Path.Combine(paths.CredentialDirectory, name)));
    }
}
