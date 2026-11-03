using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>Durable provider-context replacement. Archived messages remain recoverable; only primary-run snapshots participate in conversation replay.</summary>
public sealed record ContextCompactedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    bool IsPrimaryRun,
    string Model,
    CompactionWorkingState WorkingState,
    ChatMessage[] ArchivedMessages,
    ChatMessage[] Messages,
    int EstimatedTokensBefore,
    int EstimatedTokensAfter,
    UsageSnapshot? Usage) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
