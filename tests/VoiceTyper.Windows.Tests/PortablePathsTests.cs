using VoiceTyper.App.Storage;
using Xunit;

namespace VoiceTyper.Windows.Tests;

public sealed class PortablePathsTests
{
    [Fact]
    public void Layout_is_rooted_beside_executable()
    {
        var paths = new PortablePaths(@"D:\portable");
        Assert.Equal(Path.GetFullPath(@"D:\portable\data\settings.json"), paths.SettingsPath);
        Assert.Equal(Path.GetFullPath(@"D:\portable\data\credentials"), paths.CredentialDirectory);
        Assert.Equal(Path.GetFullPath(@"D:\portable\recordings"), paths.RecordingDirectory);
    }

    [Fact]
    public void Migration_copies_legacy_data_only_when_portable_data_is_empty()
    {
        using var area = new TemporaryDirectory();
        var legacy = Path.Combine(area.Path, "legacy");
        var portable = Path.Combine(area.Path, "portable");
        Directory.CreateDirectory(Path.Combine(legacy, "credentials"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "legacy-settings");
        File.WriteAllText(Path.Combine(legacy, "credentials", "secret.dat"), "legacy-secret");

        PortablePaths.MigrateLegacyData(legacy, portable);
        File.WriteAllText(Path.Combine(portable, "settings.json"), "portable-settings");
        PortablePaths.MigrateLegacyData(legacy, portable);

        Assert.Equal("portable-settings", File.ReadAllText(Path.Combine(portable, "settings.json")));
        Assert.Equal("legacy-secret", File.ReadAllText(Path.Combine(portable, "credentials", "secret.dat")));
        Assert.Equal("legacy-settings", File.ReadAllText(Path.Combine(legacy, "settings.json")));
    }
}
