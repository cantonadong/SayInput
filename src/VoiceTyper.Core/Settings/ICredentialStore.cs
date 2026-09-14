namespace VoiceTyper.Core.Settings;

/// <summary>Secrets must use OS-protected storage, never settings JSON or logs.</summary>
public interface ICredentialStore
{
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);
    Task WriteAsync(string key, string secret, CancellationToken cancellationToken);
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
