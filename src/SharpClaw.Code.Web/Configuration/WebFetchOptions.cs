namespace SharpClaw.Code.Web.Configuration;

/// <summary>
/// Configures the safety limits applied by the web fetch service.
/// </summary>
public sealed class WebFetchOptions
{
    /// <summary>
    /// Allows fetching loopback, private, link-local, and other non-public addresses.
    /// Off by default: a model-driven fetch must not reach cloud metadata endpoints or internal services.
    /// </summary>
    public bool AllowPrivateNetworkAccess { get; set; }

    /// <summary>
    /// Maximum number of response body bytes read; longer bodies are truncated.
    /// </summary>
    public int MaxResponseBytes { get; set; } = 5 * 1024 * 1024;
}
