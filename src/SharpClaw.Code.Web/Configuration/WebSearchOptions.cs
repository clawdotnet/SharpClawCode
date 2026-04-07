namespace SharpClaw.Code.Web.Configuration;

/// <summary>
/// Configures the default web search backend.
/// </summary>
public sealed class WebSearchOptions
{
    /// <summary>
    /// Gets or sets the provider label returned in search responses.
    /// </summary>
    public string ProviderName { get; set; } = "duckduckgo-html";

    /// <summary>
    /// Gets or sets the endpoint template used for searching.
    /// </summary>
    public string EndpointTemplate { get; set; } = "https://duckduckgo.com/html/?q={query}";

    /// <summary>
    /// Gets or sets the default user agent.
    /// </summary>
    public string UserAgent { get; set; } = "SharpClawCode/1.0";
}
