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
        return statusCode is null
            ? message
            : $"HTTP {(int)statusCode.Value} {statusCode.Value}: {message}";
    }

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
