namespace VoiceTyper.Volcengine;

/// <summary>In-memory credentials; persist secrets only through the credential store.</summary>
public sealed record VolcengineOptions
{
    public string AppId { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string ResourceId { get; init; } = "volc.bigasr.sauc.duration";
    public Uri Endpoint { get; init; } = new("wss://openspeech.bytedance.com/api/v3/sauc/bigmodel");

    public override string ToString() => "VolcengineOptions { Credentials = [redacted] }";

    internal void Validate()
    {
        if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.UserInfo.Length != 0 || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0 ||
            !((Endpoint.Scheme == "ws" && Endpoint.IsLoopback) ||
              (Endpoint.Scheme == "wss" && Endpoint.Host.Equals("openspeech.bytedance.com", StringComparison.OrdinalIgnoreCase) && Endpoint.Port == 443)))
            throw new ArgumentException("Speech endpoint must use the trusted Volcengine TLS host or a loopback WebSocket.");
        if (string.IsNullOrWhiteSpace(ApiKey) && (string.IsNullOrWhiteSpace(AppId) || string.IsNullOrWhiteSpace(AccessToken)))
            throw new ArgumentException("Configure an API key or both App ID and Access Token.");
        if (string.IsNullOrWhiteSpace(ResourceId) || new[] { AppId, AccessToken, ApiKey, ResourceId }.Any(value => value is null || value.Any(char.IsControl)))
            throw new ArgumentException("Speech credentials and resource ID must be valid HTTP header values.");
    }
}
