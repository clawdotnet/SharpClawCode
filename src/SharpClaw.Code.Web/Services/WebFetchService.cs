using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using SharpClaw.Code.Web.Abstractions;
using SharpClaw.Code.Web.Models;

namespace SharpClaw.Code.Web.Services;

/// <summary>
/// Fetches web documents and normalizes HTML into simple text content.
/// </summary>
public sealed partial class WebFetchService(HttpClient httpClient) : IWebFetchService
{
    /// <inheritdoc />
    public async Task<WebFetchDocument> FetchAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        using var response = await FetchPublicAsync(new Uri(url, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var body = await ReadBoundedBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);

        if (!ShouldTreatBodyAsHtml(contentType, body))
        {
            return new WebFetchDocument(
                Url: url,
                StatusCode: (int)response.StatusCode,
                ContentType: contentType,
                Title: null,
                Content: DecodeAndNormalize(body));
        }

        // HtmlParser is not thread-safe; use a dedicated instance per fetch for parallel tool calls/tests.
        var parser = new HtmlParser();
        var document = await parser.ParseDocumentAsync(body, cancellationToken).ConfigureAwait(false);
        var rawTitle = document.Head?.QuerySelector("title")?.TextContent ?? document.Title;
        var title = string.IsNullOrWhiteSpace(rawTitle)
            ? null
            : DecodeAndNormalize(rawTitle.Trim());
        var text = ExtractVisibleBodyText(document);

        return new WebFetchDocument(
            Url: url,
            StatusCode: (int)response.StatusCode,
            ContentType: contentType,
            Title: title,
            Content: DecodeAndNormalize(text));
    }

    /// <summary>Maximum response bytes read from one fetch; larger bodies are truncated.</summary>
    public const int MaxBodyBytes = 5 * 1024 * 1024;

    private static async Task<string> ReadBoundedBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bounded = new MemoryStream();
        var chunk = new byte[81920];
        while (bounded.Length < MaxBodyBytes)
        {
            var wanted = (int)Math.Min(chunk.Length, MaxBodyBytes - bounded.Length);
            var read = await stream.ReadAsync(chunk.AsMemory(0, wanted), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            bounded.Write(chunk, 0, read);
        }

        var encoding = System.Text.Encoding.UTF8;
        var charset = content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = System.Text.Encoding.GetEncoding(charset); }
            catch (ArgumentException) { /* Unknown charset: fall back to UTF-8. */ }
        }

        return encoding.GetString(bounded.GetBuffer(), 0, (int)bounded.Length).TrimStart('\uFEFF');
    }

    private async Task<HttpResponseMessage> FetchPublicAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            PublicNetwork.ValidateUri(uri);
            var response = await ResilientHttpGet.GetAsync(httpClient, uri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new HttpRequestException("Redirect has no destination.");
            uri = new Uri(uri, location);
        }
        throw new HttpRequestException("Too many redirects.");
    }

    /// <summary>
    /// Uses declared media type when present; otherwise sniffs common markup so plain-text responses
    /// that are actually HTML still parse correctly (typical for minimal <see cref="StringContent"/> in tests/tools).
    /// </summary>
    private static bool ShouldTreatBodyAsHtml(string? mediaType, string body)
    {
        if (LooksLikeHtmlMediaType(mediaType))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var span = body.AsSpan().TrimStart();
        if (span.IsEmpty || span[0] != '<')
        {
            return false;
        }

        return span.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
               || span.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
               || span.StartsWith("<head", StringComparison.OrdinalIgnoreCase)
               || span.Contains("<head", StringComparison.OrdinalIgnoreCase)
               || span.Contains("<body", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeHtmlMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
        {
            // Defer to body sniffing in ShouldTreatBodyAsHtml so binary responses without a content type are not parsed as HTML.
            return false;
        }

        return mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("xhtml", StringComparison.OrdinalIgnoreCase)
               || mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractVisibleBodyText(AngleSharp.Html.Dom.IHtmlDocument document)
    {
        var body = document.Body;
        if (body is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var text in body.Descendants<IText>())
        {
            if (ShouldSkipTextNode(text))
            {
                continue;
            }

            var data = text.Data;
            if (string.IsNullOrWhiteSpace(data))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(data.Trim());
        }

        return builder.ToString();
    }

    private static bool ShouldSkipTextNode(IText text)
    {
        for (var element = text.ParentElement; element is not null; element = element.ParentElement)
        {
            var name = element.LocalName;
            if (name.Equals("script", StringComparison.OrdinalIgnoreCase)
                || name.Equals("style", StringComparison.OrdinalIgnoreCase)
                || name.Equals("noscript", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string DecodeAndNormalize(string input)
        => PunctuationSpacingRegex().Replace(
            WhitespaceRegex().Replace(WebUtility.HtmlDecode(input), " ").Trim(),
            "$1");

    [GeneratedRegex("\\s+", RegexOptions.Singleline)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\s+([\\.,;:!\\?])", RegexOptions.Singleline)]
    private static partial Regex PunctuationSpacingRegex();
}
