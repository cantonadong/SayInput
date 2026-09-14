using System.Security.Cryptography;
using System.Text;
using VoiceTyper.Core.Settings;
using VoiceTyper.Windows.Storage;

namespace VoiceTyper.Windows.Security;

public sealed class DpapiCredentialStore : ICredentialStore
{
    private readonly string directory;

    public DpapiCredentialStore(string? directory = null)
    {
        this.directory = Path.GetFullPath(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VoiceTyper", "credentials"));
    }

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entropy = GetKeyHash(key);
        byte[] encrypted;
        try
        {
            await using var stream = new FileStream(GetPath(entropy), FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            encrypted = buffer.ToArray();
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }

        cancellationToken.ThrowIfCancellationRequested();
        byte[] plaintext;
        try { plaintext = ProtectedData.Unprotect(encrypted, entropy, DataProtectionScope.CurrentUser); }
        catch (CryptographicException)
        {
            throw new CryptographicException("Unable to decrypt stored credential for the current Windows user.");
        }
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public async Task WriteAsync(string key, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        var entropy = GetKeyHash(key);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        await AtomicFile.WriteAsync(GetPath(entropy), encrypted, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { File.Delete(GetPath(GetKeyHash(key))); }
        catch (DirectoryNotFoundException) { }
        return Task.CompletedTask;
    }

    private string GetPath(byte[] keyHash) => Path.Combine(directory, Convert.ToHexString(keyHash) + ".bin");

    private static byte[] GetKeyHash(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }
}

