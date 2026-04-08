namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Shared HTTP client surface helpers for model providers.
/// </summary>
public static class ProviderHttpHelpers
{
    private const string DefaultModelAlias = "default";

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

    /// <summary>
    /// Resolves a requested model, treating null/blank/default as the configured default model.
    /// </summary>
    public static string ResolveModelOrDefault(string? requestedModel, string defaultModel)
        => string.IsNullOrWhiteSpace(requestedModel) || IsDefaultModelAlias(requestedModel)
            ? defaultModel
            : requestedModel;

    /// <summary>
    /// Returns whether the model text is the runtime default-model sentinel.
    /// </summary>
    public static bool IsDefaultModelAlias(string? model)
        => string.Equals(model?.Trim(), DefaultModelAlias, StringComparison.OrdinalIgnoreCase);
}
