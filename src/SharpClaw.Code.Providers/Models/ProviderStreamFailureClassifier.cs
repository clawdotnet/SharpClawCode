using System.ClientModel;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Models;

/// <summary>
/// Classifies provider stream failures without changing the provider event contract.
/// </summary>
public static class ProviderStreamFailureClassifier
{
    private static readonly string[] StatusPropertyNames = ["StatusCode", "Status"];

    /// <summary>
    /// Returns whether an authentication status is already expired.
    /// </summary>
    public static bool IsExpired(AuthStatus authStatus, DateTimeOffset utcNow)
        => authStatus.ExpiresAtUtc is { } expiresAt && expiresAt <= utcNow;

    /// <summary>
    /// Converts an exception into stable failed-event content while preserving HTTP status detail when available.
    /// </summary>
    public static string Describe(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;
        var statusCode = TryGetStatusCode(exception);
        if (statusCode is null)
        {
            return message;
        }

        var retryAfter = TryGetRetryAfter(exception);
        return retryAfter is { } delay
            ? $"HTTP {(int)statusCode.Value} {statusCode.Value} (retry-after {(int)Math.Ceiling(delay.TotalSeconds)}s): {message}"
            : $"HTTP {(int)statusCode.Value} {statusCode.Value}: {message}";
    }

    /// <summary>
    /// Determines whether an exception represents a transient provider failure worth retrying
    /// (timeouts, connection failures, HTTP 408/429/5xx).
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns><see langword="true"/> when a retry may succeed.</returns>
    public static bool IsTransient(Exception exception)
    {
        if (IsAuthenticationFailure(exception) || IsContextOverflow(exception))
        {
            return false;
        }

        if (TryGetStatusCode(exception) is { } statusCode)
        {
            return IsTransientStatus(statusCode);
        }

        return exception is HttpRequestException or TimeoutException or IOException
            || (exception.InnerException is not null && IsTransient(exception.InnerException));
    }

    /// <summary>
    /// Determines whether a terminal <c>failed</c> provider event describes a transient failure.
    /// Events produced from HTTP errors carry an <c>HTTP {status}</c> prefix; failures without a status
    /// (connection resets, DNS errors) are treated as transient unless they look like auth or context errors.
    /// </summary>
    /// <param name="providerEvent">The failed event.</param>
    /// <returns><see langword="true"/> when a retry may succeed.</returns>
    public static bool IsTransientFailedEvent(ProviderEvent providerEvent)
    {
        if (ClassifyFailedEvent(providerEvent) != ProviderFailureKind.StreamFailed)
        {
            return false;
        }

        var match = HttpStatusPrefix.Match(providerEvent.Content ?? string.Empty);
        return !match.Success
            || IsTransientStatus((HttpStatusCode)int.Parse(match.Groups["status"].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Reads a retry-after hint embedded in a failed event's description, if any.
    /// </summary>
    /// <param name="providerEvent">The failed event.</param>
    /// <returns>The suggested delay, or <see langword="null"/>.</returns>
    public static TimeSpan? TryGetRetryAfter(ProviderEvent providerEvent)
    {
        var match = RetryAfterHint.Match(providerEvent.Content ?? string.Empty);
        return match.Success
            ? TimeSpan.FromSeconds(int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture))
            : null;
    }

    /// <summary>
    /// Reads the Retry-After header (delta-seconds or HTTP date) from an SDK or HTTP exception when available.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The suggested delay, or <see langword="null"/>.</returns>
    public static TimeSpan? TryGetRetryAfter(Exception exception)
    {
        if (exception is ClientResultException clientResultException
            && clientResultException.GetRawResponse() is { } rawResponse
            && rawResponse.Headers.TryGetValue("Retry-After", out var headerValue)
            && ParseRetryAfter(headerValue) is { } fromHeader)
        {
            return fromHeader;
        }

        if (exception.Data.Contains("Retry-After"))
        {
            switch (exception.Data["Retry-After"])
            {
                case int seconds when seconds > 0:
                    return TimeSpan.FromSeconds(seconds);
                case TimeSpan span when span > TimeSpan.Zero:
                    return span;
                case string text when ParseRetryAfter(text) is { } parsed:
                    return parsed;
            }
        }

        return exception.InnerException is null ? null : TryGetRetryAfter(exception.InnerException);
    }

    private static TimeSpan? ParseRetryAfter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
        {
            var delta = date - DateTimeOffset.UtcNow;
            return delta > TimeSpan.Zero ? delta : null;
        }

        return null;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;

    private static readonly Regex HttpStatusPrefix = new(@"^HTTP (?<status>\d{3})\b", RegexOptions.CultureInvariant);
    private static readonly Regex RetryAfterHint = new(@"\(retry-after (?<seconds>\d+)s\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// Classifies a terminal provider failed event.
    /// </summary>
    public static ProviderFailureKind ClassifyFailedEvent(ProviderEvent providerEvent)
        => LooksLikeAuthenticationFailure(providerEvent.Content)
            ? ProviderFailureKind.AuthenticationUnavailable
            : LooksLikeContextOverflow(providerEvent.Content) ? ProviderFailureKind.ContextWindowExceeded : ProviderFailureKind.StreamFailed;

    /// <summary>
    /// Returns whether an exception represents provider authentication or authorization failure.
    /// </summary>
    public static bool IsAuthenticationFailure(Exception exception)
    {
        if (exception is ProviderExecutionException { Kind: ProviderFailureKind.AuthenticationUnavailable }) return true;
        var statusCode = TryGetStatusCode(exception);
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return true;
        }

        var typeName = exception.GetType().Name;
        return typeName.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || typeName.Contains("Authentication", StringComparison.OrdinalIgnoreCase)
            || LooksLikeAuthenticationFailure(exception.Message)
            || (exception.InnerException is not null && IsAuthenticationFailure(exception.InnerException));
    }

    /// <summary>Recognizes explicit context-window rejections without treating rate limits or authentication failures as overflow.</summary>
    public static bool IsContextOverflow(Exception exception)
        => exception is ProviderExecutionException { Kind: ProviderFailureKind.ContextWindowExceeded }
            || (!IsAuthenticationFailure(exception) && (LooksLikeContextOverflow(exception.Message)
                || exception.InnerException is not null && IsContextOverflow(exception.InnerException)));

    private static bool LooksLikeContextOverflow(string? message)
        => message is not null && new[] { "context_length_exceeded", "maximum context length", "prompt is too long", "context window exceeded", "exceeds the context window", "input token count exceeds" }
            .Any(pattern => message.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikeAuthenticationFailure(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var value = message.ToLowerInvariant();
        if (Regex.IsMatch(value, @"(?:^|\W)401(?:\W|$)", RegexOptions.CultureInvariant)
            || value.Contains("unauthorized", StringComparison.Ordinal)
            || value.Contains("forbidden", StringComparison.Ordinal))
        {
            return true;
        }

        if ((value.Contains("api key", StringComparison.Ordinal)
                || value.Contains("token", StringComparison.Ordinal)
                || value.Contains("credential", StringComparison.Ordinal)
                || value.Contains("authentication", StringComparison.Ordinal))
            && (value.Contains("expired", StringComparison.Ordinal)
                || value.Contains("invalid", StringComparison.Ordinal)
                || value.Contains("missing", StringComparison.Ordinal)
                || value.Contains("failed", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static HttpStatusCode? TryGetStatusCode(Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: { } httpStatusCode })
        {
            return httpStatusCode;
        }

        if (exception is ClientResultException { Status: > 0 } clientResultException)
        {
            return (HttpStatusCode)clientResultException.Status;
        }

        var reflected = TryGetReflectedStatusCode(exception);
        if (reflected is not null)
        {
            return reflected;
        }

        return exception.InnerException is null ? null : TryGetStatusCode(exception.InnerException);
    }

    private static HttpStatusCode? TryGetReflectedStatusCode(Exception exception)
    {
        foreach (var propertyName in StatusPropertyNames)
        {
            var property = exception.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.GetValue(exception) is not { } value)
            {
                continue;
            }

            if (value is HttpStatusCode httpStatusCode)
            {
                return httpStatusCode;
            }

            if (value is int intStatusCode and > 0)
            {
                return (HttpStatusCode)intStatusCode;
            }

            if (value.GetType().IsEnum)
            {
                var enumStatusCode = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                if (enumStatusCode > 0)
                {
                    return (HttpStatusCode)enumStatusCode;
                }
            }
        }

        return null;
    }
}
