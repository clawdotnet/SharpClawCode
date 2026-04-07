namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Configures the OpenAI-compatible provider client.
/// </summary>
public sealed class OpenAiCompatibleProviderOptions
{
    /// <summary>
    /// Gets or sets the provider name used for resolution.
    /// </summary>
    public string ProviderName { get; set; } = "openai-compatible";

    /// <summary>
    /// Gets or sets the base URL for the OpenAI-compatible API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    /// <summary>
    /// Gets or sets the API key.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Gets or sets the default model id.
    /// </summary>
    public string DefaultModel { get; set; } = "gpt-4.1-mini";
}
