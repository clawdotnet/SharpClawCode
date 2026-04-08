namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Shared HTTP client surface helpers for model providers.
/// </summary>
public static class ProviderHttpHelpers
{
    /// <summary>
    /// Returns a base URL with a trailing slash when non-empty, otherwise null.
    /// </summary>
    public static string? NormalizeBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return baseUrl.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : $"{baseUrl}/";
    }
}
