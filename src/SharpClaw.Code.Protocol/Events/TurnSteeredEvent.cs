namespace SharpClaw.Code.Protocol.Events;

/// <summary>A user correction accepted at a safe provider-request boundary.</summary>
public sealed record TurnSteeredEvent(string EventId, string SessionId, string? TurnId, DateTimeOffset OccurredAtUtc, string Message)
    : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
