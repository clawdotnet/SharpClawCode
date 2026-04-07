using SharpClaw.Code.Web.Models;

namespace SharpClaw.Code.Web.Abstractions;

/// <summary>
/// Performs structured web searches through a replaceable backend.
/// </summary>
public interface IWebSearchService
{
    /// <summary>
    /// Executes a search query and returns structured results.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The structured search response.</returns>
    Task<WebSearchResponse> SearchAsync(string query, int? limit, CancellationToken cancellationToken);
}
