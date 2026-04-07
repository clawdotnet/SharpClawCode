namespace SharpClaw.Code.Web.Models;

/// <summary>
/// Represents a structured web search response.
/// </summary>
/// <param name="Query">The executed query.</param>
/// <param name="Provider">The provider label.</param>
/// <param name="Results">The structured search results.</param>
public sealed record WebSearchResponse(
    string Query,
    string Provider,
    IReadOnlyList<WebSearchResult> Results);
