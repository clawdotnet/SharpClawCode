using System.Diagnostics;

namespace SharpClaw.Code.Telemetry.Diagnostics;

/// <summary>
/// Wraps a provider call in an OpenTelemetry Activity span.
/// </summary>
public sealed class ProviderActivityScope : IDisposable
{
    private readonly Activity? _activity;

    public ProviderActivityScope(string providerName, string model, string requestId)
    {
        _activity = SharpClawActivitySource.Instance.StartActivity("sharpclaw.provider");
        _activity?.SetTag("sharpclaw.provider.name", providerName);
        _activity?.SetTag("sharpclaw.provider.model", model);
        _activity?.SetTag("sharpclaw.provider.request_id", requestId);
    }

    public void SetCompleted(long? inputTokens, long? outputTokens)
    {
        if (inputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.input", inputTokens.Value);
        if (outputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.output", outputTokens.Value);
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    public void SetError(string message)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, message);
    }

    public void Dispose() => _activity?.Dispose();
}
