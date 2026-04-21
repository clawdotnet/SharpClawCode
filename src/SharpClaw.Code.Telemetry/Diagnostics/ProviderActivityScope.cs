using System.Diagnostics;

namespace SharpClaw.Code.Telemetry.Diagnostics;

/// <summary>
/// Wraps a provider call in an OpenTelemetry Activity span.
/// </summary>
public sealed class ProviderActivityScope : IDisposable
{
    private readonly Activity? _activity;

    /// <summary>
    /// Starts a provider activity span for one provider request.
    /// </summary>
    /// <param name="providerName">Provider identifier.</param>
    /// <param name="model">Resolved model name.</param>
    /// <param name="requestId">Provider request identifier.</param>
    public ProviderActivityScope(string providerName, string model, string requestId)
    {
        _activity = SharpClawActivitySource.Instance.StartActivity("sharpclaw.provider");
        _activity?.SetTag("sharpclaw.provider.name", providerName);
        _activity?.SetTag("sharpclaw.provider.model", model);
        _activity?.SetTag("sharpclaw.provider.request_id", requestId);
    }

    /// <summary>
    /// Marks the provider span as successful and records token usage when available.
    /// </summary>
    /// <param name="inputTokens">Consumed input tokens.</param>
    /// <param name="outputTokens">Produced output tokens.</param>
    public void SetCompleted(long? inputTokens, long? outputTokens)
    {
        if (inputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.input", inputTokens.Value);
        if (outputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.output", outputTokens.Value);
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Marks the provider span as failed.
    /// </summary>
    /// <param name="message">Error detail.</param>
    public void SetError(string message)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, message);
    }

    /// <inheritdoc />
    public void Dispose() => _activity?.Dispose();
}
