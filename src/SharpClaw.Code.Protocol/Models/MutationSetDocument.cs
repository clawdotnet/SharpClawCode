namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Durable document describing all file mutations for a single checkpoint/turn group.
/// </summary>
/// <param name="SchemaVersion">Document schema version.</param>
/// <param name="Id">Mutation set identifier (typically matches <see cref="RuntimeCheckpoint.Id"/>).</param>
/// <param name="SessionId">Owning session id.</param>
/// <param name="TurnId">Associated turn id.</param>
/// <param name="CheckpointId">Associated checkpoint id.</param>
/// <param name="RecordedAtUtc">When the set was persisted.</param>
/// <param name="Operations">Ordered operations for forward application; inverse applies in reverse order.</param>
public sealed record MutationSetDocument(
    string SchemaVersion,
    string Id,
    string SessionId,
    string TurnId,
    string CheckpointId,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<FileMutationOperation> Operations);
