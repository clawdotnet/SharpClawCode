using System.Diagnostics;

namespace SharpClaw.Code.Telemetry.Diagnostics;

/// <summary>
/// Wraps a turn execution in an OpenTelemetry Activity span.
/// </summary>
public sealed class TurnActivityScope : IDisposable
{
    private readonly Activity? _activity;

    /// <summary>
    /// Starts a turn activity span.
    /// </summary>
    /// <param name="sessionId">Owning session identifier.</param>
    /// <param name="turnId">Turn identifier.</param>
    /// <param name="prompt">Optional prompt preview.</param>
    public TurnActivityScope(string sessionId, string turnId, string? prompt = null)
    {
        _activity = SharpClawActivitySource.Instance.StartActivity("sharpclaw.turn");
        _activity?.SetTag("sharpclaw.session.id", sessionId);
        _activity?.SetTag("sharpclaw.turn.id", turnId);
        if (prompt is not null)
        {
            // Truncate prompt to avoid huge spans
            _activity?.SetTag("sharpclaw.turn.prompt_preview", prompt.Length > 200 ? prompt[..200] + "..." : prompt);
        }
    }

    /// <summary>
    /// Marks the turn span as successful and records output and token usage.
    /// </summary>
    /// <param name="output">Optional turn output.</param>
    /// <param name="inputTokens">Consumed input tokens.</param>
    /// <param name="outputTokens">Produced output tokens.</param>
    public void SetOutput(string? output, long? inputTokens, long? outputTokens)
    {
        if (output is not null)
        {
            _activity?.SetTag("sharpclaw.turn.output_length", output.Length);
        }
        if (inputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.input", inputTokens.Value);
        if (outputTokens.HasValue) _activity?.SetTag("sharpclaw.tokens.output", outputTokens.Value);
        _activity?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <summary>
    /// Marks the turn span as failed and records exception details.
    /// </summary>
    /// <param name="exception">The error that terminated the turn.</param>
    public void SetError(Exception exception)
    {
        _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity?.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
        }));
    }

    /// <inheritdoc />
    public void Dispose() => _activity?.Dispose();
}
