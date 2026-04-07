namespace SharpClaw.Code.Web.Models;

/// <summary>
/// Represents a fetched web document with normalized content.
/// </summary>
/// <param name="Url">The fetched URL.</param>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="ContentType">The response content type.</param>
/// <param name="Title">The document title, if available.</param>
/// <param name="Content">The normalized text content.</param>
public sealed record WebFetchDocument(
    string Url,
    int StatusCode,
    string? ContentType,
    string? Title,
    string Content);
