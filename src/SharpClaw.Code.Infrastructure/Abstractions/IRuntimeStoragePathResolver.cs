namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Resolves tenant-aware durable storage paths for SharpClaw runtime state.
/// </summary>
public interface IRuntimeStoragePathResolver
{
    /// <summary>Gets the tenant-aware SharpClaw storage root for a workspace.</summary>
    string GetSharpClawRoot(string workspacePath);

    /// <summary>Gets the root directory containing persisted sessions.</summary>
    string GetSessionsRoot(string workspacePath);

    /// <summary>Gets the root directory for a specific session.</summary>
    string GetSessionRoot(string workspacePath, string sessionId);

    /// <summary>Gets the session snapshot JSON path.</summary>
    string GetSessionSnapshotPath(string workspacePath, string sessionId);

    /// <summary>Gets the per-session turn lock path.</summary>
    string GetSessionTurnLockPath(string workspacePath, string sessionId);

    /// <summary>Gets the append-only events log path.</summary>
    string GetEventsPath(string workspacePath, string sessionId);

    /// <summary>Gets the checkpoints directory path.</summary>
    string GetCheckpointsRoot(string workspacePath, string sessionId);

    /// <summary>Gets one checkpoint JSON path.</summary>
    string GetCheckpointPath(string workspacePath, string sessionId, string checkpointId);

    /// <summary>Gets the mutations directory path.</summary>
    string GetMutationsRoot(string workspacePath, string sessionId);

    /// <summary>Gets one mutation-set JSON path.</summary>
    string GetMutationSetPath(string workspacePath, string sessionId, string mutationSetId);

    /// <summary>Gets the workspace attachment file path.</summary>
    string GetWorkspaceActiveSessionPath(string workspacePath);

    /// <summary>Gets the share snapshot directory path.</summary>
    string GetSharesRoot(string workspacePath);

    /// <summary>Gets one share snapshot JSON path.</summary>
    string GetShareSnapshotPath(string workspacePath, string shareId);

    /// <summary>Gets the workspace todo snapshot path.</summary>
    string GetWorkspaceTodosPath(string workspacePath);

    /// <summary>Gets the workspace todo lock path.</summary>
    string GetWorkspaceTodosLockPath(string workspacePath);

    /// <summary>Gets the workspace knowledge directory path.</summary>
    string GetWorkspaceKnowledgeRoot(string workspacePath);

    /// <summary>Gets the workspace exports directory path.</summary>
    string GetExportsRoot(string workspacePath);

    /// <summary>Gets the workspace telemetry directory path.</summary>
    string GetTelemetryRoot(string workspacePath);

    /// <summary>Gets the SQLite database path used by usage metering.</summary>
    string GetUsageMeteringDatabasePath(string workspacePath);

    /// <summary>Gets the SQLite database path used by alternate session and event stores.</summary>
    string GetSessionStoreDatabasePath(string workspacePath);

    /// <summary>Gets the tool package catalog directory path.</summary>
    string GetToolPackagesRoot(string workspacePath);

    /// <summary>Gets the extracted package directory path for a packaged tool install.</summary>
    string GetExtractedToolPackageRoot(string workspacePath, string packageId, string version);

    /// <summary>Gets the user-level SharpClaw root with any active tenant partition applied.</summary>
    string GetUserSharpClawRoot();
}
