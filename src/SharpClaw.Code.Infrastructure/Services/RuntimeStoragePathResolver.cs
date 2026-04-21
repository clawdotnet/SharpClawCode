using System.Security.Cryptography;
using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <inheritdoc />
public sealed class RuntimeStoragePathResolver(
    IPathService pathService,
    IUserProfilePaths userProfilePaths,
    IRuntimeHostContextAccessor hostContextAccessor) : IRuntimeStoragePathResolver
{
    /// <inheritdoc />
    public string GetSharpClawRoot(string workspacePath)
    {
        var workspace = pathService.GetFullPath(workspacePath);
        var hostContext = hostContextAccessor.Current;
        var baseRoot = string.IsNullOrWhiteSpace(hostContext?.StorageRoot)
            ? workspace
            : pathService.Combine(
                pathService.GetFullPath(hostContext.StorageRoot!),
                "workspaces",
                BuildWorkspaceKey(workspace));

        var sharpClawRoot = pathService.Combine(baseRoot, ".sharpclaw");
        return string.IsNullOrWhiteSpace(hostContext?.TenantId)
            ? sharpClawRoot
            : pathService.Combine(sharpClawRoot, "tenants", SanitizeSegment(hostContext!.TenantId!));
    }

    /// <inheritdoc />
    public string GetSessionsRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "sessions");

    /// <inheritdoc />
    public string GetSessionRoot(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionsRoot(workspacePath), sessionId);

    /// <inheritdoc />
    public string GetSessionSnapshotPath(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(workspacePath, sessionId), "session.json");

    /// <inheritdoc />
    public string GetSessionTurnLockPath(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(workspacePath, sessionId), ".turn.lock");

    /// <inheritdoc />
    public string GetEventsPath(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(workspacePath, sessionId), "events.ndjson");

    /// <inheritdoc />
    public string GetCheckpointsRoot(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(workspacePath, sessionId), "checkpoints");

    /// <inheritdoc />
    public string GetCheckpointPath(string workspacePath, string sessionId, string checkpointId)
        => pathService.Combine(GetCheckpointsRoot(workspacePath, sessionId), $"{checkpointId}.json");

    /// <inheritdoc />
    public string GetMutationsRoot(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionRoot(workspacePath, sessionId), "mutations");

    /// <inheritdoc />
    public string GetMutationSetPath(string workspacePath, string sessionId, string mutationSetId)
        => pathService.Combine(GetMutationsRoot(workspacePath, sessionId), $"{mutationSetId}.json");

    /// <inheritdoc />
    public string GetWorkspaceActiveSessionPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "active-session.json");

    /// <inheritdoc />
    public string GetSharesRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "shares");

    /// <inheritdoc />
    public string GetShareSnapshotPath(string workspacePath, string shareId)
        => pathService.Combine(GetSharesRoot(workspacePath), $"{shareId}.json");

    /// <inheritdoc />
    public string GetWorkspaceTodosPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "tasks.json");

    /// <inheritdoc />
    public string GetWorkspaceTodosLockPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), ".tasks.lock");

    /// <inheritdoc />
    public string GetWorkspaceKnowledgeRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "knowledge");

    /// <inheritdoc />
    public string GetExportsRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "exports");

    /// <inheritdoc />
    public string GetTelemetryRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "telemetry");

    /// <inheritdoc />
    public string GetUsageMeteringDatabasePath(string workspacePath)
        => pathService.Combine(GetTelemetryRoot(workspacePath), "usage-metering.db");

    /// <inheritdoc />
    public string GetSessionStoreDatabasePath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "session-store.db");

    /// <inheritdoc />
    public string GetToolPackagesRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "tool-packages");

    /// <inheritdoc />
    public string GetExtractedToolPackageRoot(string workspacePath, string packageId, string version)
        => pathService.Combine(
            GetToolPackagesRoot(workspacePath),
            "extracted",
            $"{SanitizeSegment(packageId)}-{SanitizeSegment(version)}");

    /// <inheritdoc />
    public string GetUserSharpClawRoot()
    {
        var root = userProfilePaths.GetUserSharpClawRoot();
        var hostContext = hostContextAccessor.Current;
        return string.IsNullOrWhiteSpace(hostContext?.TenantId)
            ? root
            : pathService.Combine(root, "tenants", SanitizeSegment(hostContext!.TenantId!));
    }

    private string BuildWorkspaceKey(string workspacePath)
    {
        var normalized = pathService.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = pathService.GetFileName(normalized);
        var slug = string.IsNullOrWhiteSpace(name) ? "workspace" : SanitizeSegment(name);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..12];
        return $"{slug}-{hash}";
    }

    private static string SanitizeSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "default" : result;
    }
}
