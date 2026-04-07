namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Portable session bundle manifest (offline sharing, no hosted service).
/// </summary>
/// <param name="SchemaVersion">Bundle schema version.</param>
/// <param name="CreatedAtUtc">UTC creation time.</param>
/// <param name="WorkspaceHint">Optional non-authoritative workspace label.</param>
/// <param name="SessionId">Session identifier inside the bundle.</param>
/// <param name="SessionSnapshotRelativePath">Path to <c>session.json</c> within the archive.</param>
/// <param name="EventsRelativePath">Path to <c>events.ndjson</c> within the archive.</param>
/// <param name="CheckpointsDirectoryRelativePath">Path to checkpoints directory.</param>
/// <param name="MutationsDirectoryRelativePath">Path to mutations directory.</param>
/// <param name="ExtraNotes">Optional human notes.</param>
public sealed record SessionBundleManifest(
    string SchemaVersion,
    DateTimeOffset CreatedAtUtc,
    string? WorkspaceHint,
    string SessionId,
    string SessionSnapshotRelativePath,
    string EventsRelativePath,
    string CheckpointsDirectoryRelativePath,
    string MutationsDirectoryRelativePath,
    string? ExtraNotes);
