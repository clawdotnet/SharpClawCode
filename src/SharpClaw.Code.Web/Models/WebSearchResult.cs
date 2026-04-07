namespace SharpClaw.Code.Web.Models;

/// <summary>
/// Represents a single structured web search result.
/// </summary>
/// <param name="Title">The result title.</param>
/// <param name="Url">The resolved result URL.</param>
/// <param name="Snippet">The result snippet, if available.</param>
public sealed record WebSearchResult(
    string Title,
    string Url,
    string? Snippet);
