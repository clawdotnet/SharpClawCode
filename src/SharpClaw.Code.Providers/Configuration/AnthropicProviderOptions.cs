namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Configures the Anthropic provider client.
/// </summary>
public sealed class AnthropicProviderOptions
{
    /// <summary>
    /// Gets or sets the provider name used for resolution.
    /// </summary>
    public string ProviderName { get; set; } = "anthropic";

    /// <summary>
    /// Gets or sets the Anthropic base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";

    /// <summary>
    /// Gets or sets the API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the Anthropics API version header value.
    /// </summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// Gets or sets the default model id.
    /// </summary>
    public string DefaultModel { get; set; } = "claude-3-7-sonnet-latest";
}
