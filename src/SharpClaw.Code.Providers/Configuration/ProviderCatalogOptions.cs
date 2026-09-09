namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Configures provider defaults and model alias mappings.
/// </summary>
public sealed class ProviderCatalogOptions
{
    /// <summary>
    /// Gets or sets the default provider used when no provider name is specified.
    /// </summary>
    public string DefaultProvider { get; set; } = "openai-compatible";

    /// <summary>
    /// Gets the ordered provider names to try after the requested provider fails.
    /// </summary>
    public List<string> FallbackProviders { get; } = [];

    /// <summary>
    /// Gets optional model ids keyed by fallback provider name.
    /// </summary>
    public Dictionary<string, string> FallbackModels { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the configured model aliases.
    /// </summary>
    public Dictionary<string, ModelAliasDefinition> ModelAliases { get; } = new(StringComparer.OrdinalIgnoreCase);
}
