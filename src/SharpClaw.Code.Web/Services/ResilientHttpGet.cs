using System.Net;

namespace SharpClaw.Code.Web.Services;

/// <summary>
/// Minimal transient retry for idempotent HTTP GETs (network brownouts, 429/503).
/// </summary>
internal static class ResilientHttpGet
{
    private const int MaxAttempts = 3;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMilliseconds(150);

    public static async Task<HttpResponseMessage> GetAsync(
        HttpClient httpClient,
        string requestUri,
        CancellationToken cancellationToken)
    {
        var delay = InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await httpClient.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < MaxAttempts)
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay;
                continue;
            }

            if (attempt < MaxAttempts && IsTransient(response.StatusCode))
            {
                response.Dispose();
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay;
                continue;
            }

            return response;
        }
    }

    private static bool IsTransient(HttpStatusCode code)
        => code is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway;
}
