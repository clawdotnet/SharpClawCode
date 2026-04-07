using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Web.Abstractions;
using SharpClaw.Code.Web.Configuration;
using SharpClaw.Code.Web.Models;

namespace SharpClaw.Code.Web.Services;

/// <summary>
/// Executes conservative HTML-backed web searches and returns structured results.
/// </summary>
public sealed partial class WebSearchService(
    HttpClient httpClient,
    IOptions<WebSearchOptions> options) : IWebSearchService
{
    /// <inheritdoc />
    public async Task<WebSearchResponse> SearchAsync(string query, int? limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var resolvedOptions = options.Value;
        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(resolvedOptions.UserAgent);
        }

        var endpoint = resolvedOptions.EndpointTemplate.Replace("{query}", Uri.EscapeDataString(query), StringComparison.Ordinal);
        using var response = await ResilientHttpGet.GetAsync(httpClient, endpoint, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var linkMatches = ResultLinkRegex().Matches(html);
        var snippetMatches = ResultSnippetRegex().Matches(html);
        var results = new List<WebSearchResult>();

        for (var index = 0; index < linkMatches.Count && results.Count < limit.GetValueOrDefault(5); index++)
        {
            var linkMatch = linkMatches[index];
            var title = NormalizeHtml(linkMatch.Groups["title"].Value);
            var url = WebUtility.HtmlDecode(linkMatch.Groups["url"].Value);
            var snippet = index < snippetMatches.Count
                ? NormalizeHtml(snippetMatches[index].Groups["snippet"].Value)
                : null;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            results.Add(new WebSearchResult(title, url, string.IsNullOrWhiteSpace(snippet) ? null : snippet));
        }

        return new WebSearchResponse(query, resolvedOptions.ProviderName, results);
    }

    private static string NormalizeHtml(string value)
        => WebUtility.HtmlDecode(TagRegex().Replace(value, string.Empty)).Trim();

    [GeneratedRegex("<a[^>]*class=\"[^\"]*result__a[^\"]*\"[^>]*href=\"(?<url>[^\"]+)\"[^>]*>(?<title>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ResultLinkRegex();

    [GeneratedRegex("<a[^>]*class=\"[^\"]*result__snippet[^\"]*\"[^>]*>(?<snippet>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ResultSnippetRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();
}
