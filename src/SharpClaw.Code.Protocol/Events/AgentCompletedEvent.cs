using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when an agent instance completes execution.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="AgentId">The completed agent identifier.</param>
/// <param name="Succeeded">Indicates whether the agent completed successfully.</param>
/// <param name="Summary">A concise completion summary, if available.</param>
/// <param name="Usage">The usage snapshot attributed to the agent, if any.</param>
public sealed record AgentCompletedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string AgentId,
    bool Succeeded,
    string? Summary,
    UsageSnapshot? Usage) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
