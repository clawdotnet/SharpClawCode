using SharpClaw.Code.Web.Models;

namespace SharpClaw.Code.Web.Abstractions;

/// <summary>
/// Fetches structured web documents through a replaceable backend.
/// </summary>
public interface IWebFetchService
{
    /// <summary>
    /// Fetches a web document and returns structured content.
    /// </summary>
    /// <param name="url">The URL to fetch.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fetched document.</returns>
    Task<WebFetchDocument> FetchAsync(string url, CancellationToken cancellationToken);
}
