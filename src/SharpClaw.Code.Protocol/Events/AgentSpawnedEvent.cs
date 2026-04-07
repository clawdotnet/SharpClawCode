namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when the runtime spawns an agent instance.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="AgentId">The agent instance identifier.</param>
/// <param name="AgentKind">The logical kind of agent that was spawned.</param>
/// <param name="ParentAgentId">The parent agent identifier, if any.</param>
public sealed record AgentSpawnedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string AgentId,
    string AgentKind,
    string? ParentAgentId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
