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
            : pathService.Combine(sharpClawRoot, "tenants", BuildTenantSegment(hostContext!.TenantId!));
    }

    /// <inheritdoc />
    public string GetSessionsRoot(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "sessions");

    /// <inheritdoc />
    public string GetSessionRoot(string workspacePath, string sessionId)
        => pathService.Combine(GetSessionsRoot(workspacePath), ValidateSessionId(sessionId));

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
    public string GetScheduledPromptsPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "scheduled-prompts.json");

    /// <inheritdoc />
    public string GetScheduledPromptsLockPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), ".scheduled-prompts.lock");

    /// <inheritdoc />
    public string GetEvolutionProposalsPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), "evolution-proposals.json");

    /// <inheritdoc />
    public string GetEvolutionProposalsLockPath(string workspacePath)
        => pathService.Combine(GetSharpClawRoot(workspacePath), ".evolution-proposals.lock");

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
            : pathService.Combine(root, "tenants", BuildTenantSegment(hostContext!.TenantId!));
    }

    private string BuildWorkspaceKey(string workspacePath)
    {
        var normalized = pathService.GetFullPath(workspacePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = pathService.GetFileName(normalized);
        var slug = string.IsNullOrWhiteSpace(name) ? "workspace" : SanitizeSegment(name);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..12];
        return $"{slug}-{hash}";
    }

    /// <summary>
    /// Maps a tenant id to a directory name. Ids that survive sanitization unchanged keep their readable name;
    /// any id that sanitization alters gets a hash suffix so distinct tenants (for example "acme.corp" and
    /// "acme/corp") can never share a storage directory.
    /// </summary>
    private static string BuildTenantSegment(string tenantId)
    {
        var sanitized = SanitizeSegment(tenantId);
        if (string.Equals(sanitized, tenantId, StringComparison.Ordinal))
        {
            return sanitized;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tenantId))).ToLowerInvariant()[..12];
        // '~' can never appear in an id that passes sanitization unchanged, so hashed names live in their own
        // namespace: no literal tenant id can claim another tenant's generated directory name.
        return $"{sanitized}~{hash}";
    }

    private static string ValidateSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        // Windows strips trailing dots and spaces, so ids such as ".. " or "a." would resolve to a different
        // (possibly parent) directory; reject any id that does not survive that normalization unchanged.
        if (sessionId.Trim(' ', '.').Length == 0
            || sessionId.EndsWith('.')
            || sessionId.EndsWith(' ')
            || sessionId.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':']) >= 0
            || sessionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.IsPathRooted(sessionId))
        {
            throw new ArgumentException($"Session id '{sessionId}' is not a valid single path segment.", nameof(sessionId));
        }

        return sessionId;
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
