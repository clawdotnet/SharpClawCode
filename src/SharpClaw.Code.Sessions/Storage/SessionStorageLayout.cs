using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Builds local storage paths under the workspace <c>.sharpclaw</c> directory.
/// </summary>
public static class SessionStorageLayout
{
    /// <summary>Returns <c>{workspace}/.sharpclaw</c>.</summary>
    public static string GetSharpClawRoot(IPathService pathService, string workspacePath)
        => pathService.Combine(workspacePath, ".sharpclaw");

    /// <summary>Returns the directory containing all session folders.</summary>
    public static string GetSessionsRoot(IPathService pathService, string workspacePath)
        => pathService.Combine(GetSharpClawRoot(pathService, workspacePath), "sessions");

    /// <summary>Returns the root directory for a single session id.</summary>
    public static string GetSessionRoot(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionsRoot(pathService, workspacePath), sessionId);

    /// <summary>Returns the durable <c>session.json</c> snapshot path.</summary>
    public static string GetSessionSnapshotPath(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(pathService, workspacePath, sessionId), "session.json");

    /// <summary>Returns the cross-process lock file path used to serialize turns for a session.</summary>
    public static string GetSessionTurnLockPath(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(pathService, workspacePath, sessionId), ".turn.lock");

    /// <summary>Returns the append-only NDJSON event log path.</summary>
    public static string GetEventsPath(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(pathService, workspacePath, sessionId), "events.ndjson");

    /// <summary>Returns the checkpoints directory for a session.</summary>
    public static string GetCheckpointsRoot(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(pathService, workspacePath, sessionId), "checkpoints");

    /// <summary>Returns a single checkpoint payload path.</summary>
    public static string GetCheckpointPath(IPathService pathService, string workspacePath, string sessionId, string checkpointId)
        => pathService.Combine(GetCheckpointsRoot(pathService, workspacePath, sessionId), $"{checkpointId}.json");

    /// <summary>Returns the directory holding reversible mutation-set JSON documents.</summary>
    public static string GetMutationsRoot(IPathService pathService, string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(pathService, workspacePath, sessionId), "mutations");

    /// <summary>Returns the path for one mutation set document (checkpoint-aligned id).</summary>
    public static string GetMutationSetPath(IPathService pathService, string workspacePath, string sessionId, string mutationSetId)
        => pathService.Combine(GetMutationsRoot(pathService, workspacePath, sessionId), $"{mutationSetId}.json");

    /// <summary>Returns the workspace attachment pointer (<c>active-session.json</c>).</summary>
    public static string GetWorkspaceActiveSessionPath(IPathService pathService, string workspacePath)
        => pathService.Combine(GetSharpClawRoot(pathService, workspacePath), "active-session.json");

    /// <summary>Returns the share snapshot directory.</summary>
    public static string GetSharesRoot(IPathService pathService, string workspacePath)
        => pathService.Combine(GetSharpClawRoot(pathService, workspacePath), "shares");

    /// <summary>Returns a shared session snapshot payload path.</summary>
    public static string GetShareSnapshotPath(IPathService pathService, string workspacePath, string shareId)
        => pathService.Combine(GetSharesRoot(pathService, workspacePath), $"{shareId}.json");
}
