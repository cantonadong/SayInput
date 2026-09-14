using System.Security.Cryptography;
using System.Text;
using VoiceTyper.Core.Settings;
using VoiceTyper.Windows.Security;
using VoiceTyper.Windows.Settings;
using Xunit;

namespace VoiceTyper.Windows.Tests.Security;

public sealed class DpapiCredentialStoreTests
{
    [Fact]
    public async Task Delete_before_first_write_does_not_create_storage()
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "not-created");
        await new DpapiCredentialStore(directory).DeleteAsync("asr", default);
        Assert.False(Directory.Exists(directory));
    }
    [Fact]
    public async Task Missing_credential_returns_null()
    {
        using var temp = new TemporaryDirectory();
        Assert.Null(await new DpapiCredentialStore(Path.Combine(temp.Path, "credentials")).ReadAsync("asr", default));
    }

    [Fact]
    public async Task Roundtrip_update_and_delete_survive_new_store_instances()
    {
        using var temp = new TemporaryDirectory();
        var store = new DpapiCredentialStore(temp.Path);
        await store.WriteAsync("asr", "synthetic-中文-first", default);
        Assert.Equal("synthetic-中文-first", await new DpapiCredentialStore(temp.Path).ReadAsync("asr", default));
        await store.WriteAsync("asr", "synthetic-second", default);
        Assert.Equal("synthetic-second", await store.ReadAsync("asr", default));
        await store.DeleteAsync("asr", default);
        await store.DeleteAsync("asr", default);
        Assert.Null(await store.ReadAsync("asr", default));
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Stored_secret_is_DPAPI_ciphertext_and_absent_from_settings()
    {
        using var temp = new TemporaryDirectory();
        var secret = "synthetic-secret-for-local-test-only-123";
        var credentials = Path.Combine(temp.Path, "credentials");
        await new DpapiCredentialStore(credentials).WriteAsync("asr", secret, default);
        var bytes = await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(credentials)));
        Assert.DoesNotContain(secret, Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(secret, Encoding.Unicode.GetString(bytes));
        var entropy = SHA256.HashData(Encoding.UTF8.GetBytes("asr"));
        var decrypted = ProtectedData.Unprotect(bytes, entropy, DataProtectionScope.CurrentUser);
        try { Assert.Equal(secret, Encoding.UTF8.GetString(decrypted)); }
        finally { CryptographicOperations.ZeroMemory(decrypted); }
        var path = Path.Combine(temp.Path, "settings.json");
        await new JsonSettingsStore(path).SaveAsync(new AppSettings(), default);
        Assert.DoesNotContain(secret, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task Keys_cannot_escape_storage_directory_and_are_independent()
    {
        using var temp = new TemporaryDirectory();
        var directory = Path.Combine(temp.Path, "credentials");
        var store = new DpapiCredentialStore(directory);
        await store.WriteAsync("../escape", "synthetic-one", default);
        await store.WriteAsync("normal", "synthetic-two", default);
        Assert.Equal("synthetic-one", await store.ReadAsync("../escape", default));
        Assert.Equal("synthetic-two", await store.ReadAsync("normal", default));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
        Assert.Empty(Directory.GetFiles(temp.Path));
    }

    [Fact]
    public async Task Corrupt_ciphertext_fails_without_returning_plaintext_or_deleting_file()
    {
        using var temp = new TemporaryDirectory();
        var store = new DpapiCredentialStore(temp.Path);
        await store.WriteAsync("asr", "synthetic-only", default);
        var path = Assert.Single(Directory.GetFiles(temp.Path));
        await File.WriteAllTextAsync(path, "corrupt-synthetic-value");
        var error = await Assert.ThrowsAsync<CryptographicException>(() => store.ReadAsync("asr", default));
        Assert.DoesNotContain("corrupt-synthetic-value", error.ToString());
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Cancelled_operations_preserve_stored_secret()
    {
        using var temp = new TemporaryDirectory();
        var store = new DpapiCredentialStore(temp.Path);
        await store.WriteAsync("asr", "synthetic-original", default);
        var cancelled = new CancellationToken(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteAsync("asr", "replacement", cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DeleteAsync("asr", cancelled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAsync("asr", cancelled));
        Assert.Equal("synthetic-original", await store.ReadAsync("asr", default));
        Assert.Single(Directory.GetFiles(temp.Path));
    }
}

