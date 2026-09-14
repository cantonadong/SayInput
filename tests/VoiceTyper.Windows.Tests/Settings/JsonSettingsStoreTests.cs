using System.Text.Json;
using VoiceTyper.Core.Settings;
using VoiceTyper.Windows.Settings;
using Xunit;

namespace VoiceTyper.Windows.Tests.Settings;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task Missing_file_returns_defaults_without_creating_file()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "missing", "settings.json");
        var settings = await new JsonSettingsStore(path).LoadAsync(default);
        Assert.True(settings.Enabled);
        Assert.True(settings.ShowPartial);
        Assert.True(settings.EnableAudioWarmup);
        Assert.False(settings.StartWithWindows);
        Assert.False(settings.EnableDeviceWarmup);
        Assert.Null(settings.MicrophoneDeviceId);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("{invalid")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"Enabled\":\"yes\"}")]
    public async Task Corrupt_json_returns_defaults_and_keeps_original(string json)
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(path, json);
        Assert.Equal(new AppSettings(), await new JsonSettingsStore(path).LoadAsync(default));
        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Partial_config_uses_defaults_for_missing_fields()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{\"ShowPartial\":false}");
        var settings = await new JsonSettingsStore(path).LoadAsync(default);
        Assert.False(settings.ShowPartial);
        Assert.True(settings.Enabled);
        Assert.True(settings.EnableAudioWarmup);
    }

    [Fact]
    public async Task Save_and_reload_preserve_all_preferences_and_replace_old_file()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "nested", "settings.json");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new AppSettings(), default);
        var desired = new AppSettings(false, true, false, "楹﹀厠椋?123", false, true);
        await store.SaveAsync(desired, default);
        Assert.Equal(desired, await new JsonSettingsStore(path).LoadAsync(default));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task Cancelled_save_preserves_existing_settings()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(new AppSettings(MicrophoneDeviceId: "original"), default);
        var original = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(new AppSettings(), new CancellationToken(true)));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Locked_destination_preserves_old_file_and_cleans_temporary_file()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var store = new JsonSettingsStore(path);
        var original = new AppSettings(MicrophoneDeviceId: "original");
        await store.SaveAsync(original, default);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(new AppSettings(), default));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(original, await store.LoadAsync(default));
        Assert.Single(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Parallel_writes_leave_a_complete_document()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        var candidates = Enumerable.Range(0, 12).Select(i => new AppSettings(MicrophoneDeviceId: new string((char)('A' + i), 4096))).ToArray();
        await Task.WhenAll(candidates.Select(s => new JsonSettingsStore(path).SaveAsync(s, default)));
        Assert.Contains(await new JsonSettingsStore(path).LoadAsync(default), candidates);
        Assert.Single(Directory.GetFiles(temp.Path));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Unknown_secret_fields_are_not_written_back()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "settings.json");
        await File.WriteAllTextAsync(path, "{\"ApiKey\":\"synthetic-do-not-persist\",\"Enabled\":true}");
        var store = new JsonSettingsStore(path);
        await store.SaveAsync(await store.LoadAsync(default), default);
        var json = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("synthetic-do-not-persist", json);
        Assert.DoesNotContain("ApiKey", json);
    }

    [Fact]
    public async Task Cancelled_load_is_not_reported_as_defaults()
    {
        using var temp = new TemporaryDirectory();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonSettingsStore(Path.Combine(temp.Path, "settings.json")).LoadAsync(new CancellationToken(true)));
    }
}

