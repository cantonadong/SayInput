using System.IO;

namespace VoiceTyper.App.Storage;

public sealed class PortablePaths
{
    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string SettingsPath { get; }
    public string CredentialDirectory { get; }
    public string RecordingDirectory { get; }
    public string ResourceDirectory { get; }

    public PortablePaths(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? AppContext.BaseDirectory);
        DataDirectory = Path.Combine(RootDirectory, "data");
        SettingsPath = Path.Combine(DataDirectory, "settings.json");
        CredentialDirectory = Path.Combine(DataDirectory, "credentials");
        RecordingDirectory = Path.Combine(RootDirectory, "recordings");
        ResourceDirectory = Path.Combine(RootDirectory, "resources");
    }

    public void EnsureAndMigrate()
    {
        var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceTyper");
        MigrateLegacyData(legacy, DataDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(CredentialDirectory);
        Directory.CreateDirectory(RecordingDirectory);
    }

    internal static void MigrateLegacyData(string legacyDirectory, string portableDataDirectory)
    {
        if (!Directory.Exists(legacyDirectory)) return;
        CopyIfMissing(Path.Combine(legacyDirectory, "settings.json"), Path.Combine(portableDataDirectory, "settings.json"));
        var sourceCredentials = Path.Combine(legacyDirectory, "credentials");
        if (!Directory.Exists(sourceCredentials)) return;
        foreach (var source in Directory.EnumerateFiles(sourceCredentials))
            CopyIfMissing(source, Path.Combine(portableDataDirectory, "credentials", Path.GetFileName(source)));
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (!File.Exists(source) || File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".migrate-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temporary, false); File.Move(temporary, destination); }
        finally { try { File.Delete(temporary); } catch { } }
    }
}
