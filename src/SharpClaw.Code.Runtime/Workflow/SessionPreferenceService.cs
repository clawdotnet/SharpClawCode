using System.Text.Json;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <inheritdoc />
public sealed class SessionPreferenceService(
    ISessionStore sessionStore,
    ISessionCoordinator sessionCoordinator,
    ISessionUpdateCoordinator? sessionUpdateCoordinator = null) : ISessionPreferenceService
{
    // Preference edits can race a running turn; the coordinator serializes them and keeps them from being
    // overwritten by the turn's stale session copy.
    private Task<T> RunScopedAsync<T>(string workspaceRoot, string sessionId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => sessionUpdateCoordinator is null
            ? action(cancellationToken)
            : sessionUpdateCoordinator.RunAsync(workspaceRoot, sessionId, action, cancellationToken);

    /// <inheritdoc />
    public async Task<PermissionStatusReport> GetPermissionStatusAsync(
        string workspaceRoot,
        string? sessionId,
        PermissionMode fallbackPermissionMode,
        ApprovalSettings? approvalSettings,
        string? currentModel,
        CancellationToken cancellationToken)
    {
        var attachedSessionId = await sessionCoordinator.GetAttachedSessionIdAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var session = await ResolveSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return BuildReport(session, attachedSessionId, fallbackPermissionMode, approvalSettings, currentModel);
    }

    /// <inheritdoc />
    public async Task<PermissionStatusReport> GrantTrustAsync(
        string workspaceRoot,
        string? sessionId,
        TrustedSourceKind kind,
        string name,
        PermissionMode fallbackPermissionMode,
        ApprovalSettings? approvalSettings,
        string? currentModel,
        CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var key = kind == TrustedSourceKind.Plugin
                ? SharpClawWorkflowMetadataKeys.TrustedPluginNamesJson
                : SharpClawWorkflowMetadataKeys.TrustedMcpServerNamesJson;
            var names = ReadStringArray(metadata, key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            names.Add(name.Trim());
            metadata[key] = JsonSerializer.Serialize(names.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(), ProtocolJsonContext.Default.StringArray);
            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            var attachedSessionId = await sessionCoordinator.GetAttachedSessionIdAsync(workspaceRoot, ct).ConfigureAwait(false);
            return BuildReport(session, attachedSessionId, fallbackPermissionMode, approvalSettings, currentModel);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PermissionStatusReport> RevokeTrustAsync(
        string workspaceRoot,
        string? sessionId,
        TrustedSourceKind kind,
        string name,
        PermissionMode fallbackPermissionMode,
        ApprovalSettings? approvalSettings,
        string? currentModel,
        CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var key = kind == TrustedSourceKind.Plugin
                ? SharpClawWorkflowMetadataKeys.TrustedPluginNamesJson
                : SharpClawWorkflowMetadataKeys.TrustedMcpServerNamesJson;
            var names = ReadStringArray(metadata, key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            names.Remove(name.Trim());
            metadata[key] = JsonSerializer.Serialize(names.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase).ToArray(), ProtocolJsonContext.Default.StringArray);
            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            var attachedSessionId = await sessionCoordinator.GetAttachedSessionIdAsync(workspaceRoot, ct).ConfigureAwait(false);
            return BuildReport(session, attachedSessionId, fallbackPermissionMode, approvalSettings, currentModel);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SessionModelPreference> SetModelPreferenceAsync(
        string workspaceRoot,
        string? sessionId,
        string model,
        CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var preference = new SessionModelPreference(model.Trim(), DateTimeOffset.UtcNow);
            metadata[SharpClawWorkflowMetadataKeys.SessionModelPreferenceJson] = JsonSerializer.Serialize(preference, ProtocolJsonContext.Default.SessionModelPreference);
            session = session with { Metadata = metadata, UpdatedAtUtc = preference.UpdatedAtUtc };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            return preference;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ClearModelPreferenceAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            if (session.Metadata is null)
            {
                return false;
            }

            var metadata = new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var removed = metadata.Remove(SharpClawWorkflowMetadataKeys.SessionModelPreferenceJson);
            if (!removed)
            {
                return false;
            }

            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PermissionMode> SetPreferredPermissionModeAsync(
        string workspaceRoot,
        string? sessionId,
        PermissionMode permissionMode,
        CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            metadata[SharpClawWorkflowMetadataKeys.PreferredPermissionMode] = permissionMode.ToString();
            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            return permissionMode;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApprovalSettings> SetApprovalSettingsAsync(
        string workspaceRoot,
        string? sessionId,
        ApprovalSettings approvalSettings,
        CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var normalized = ApprovalSettingsResolver.Normalize(approvalSettings);

            if (normalized.AutoApproveScopes.Count == 0)
            {
                metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson);
            }
            else
            {
                metadata[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson] = JsonSerializer.Serialize(
                    normalized.AutoApproveScopes.ToList(),
                    ProtocolJsonContext.Default.ListApprovalScope);
            }

            if (normalized.AutoApproveBudget is null)
            {
                metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget);
            }
            else
            {
                metadata[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget] = normalized.AutoApproveBudget.Value.ToString();
            }

            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            return normalized;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> ClearApprovalSettingsAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
    {
        var target = await RequireSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return await RunScopedAsync(workspaceRoot, target.Id, async ct =>
        {
            var session = await RequireSessionAsync(workspaceRoot, target.Id, ct).ConfigureAwait(false);
            if (session.Metadata is null)
            {
                return false;
            }

            var metadata = new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            var removedScopes = metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson);
            var removedBudget = metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget);
            if (!removedScopes && !removedBudget)
            {
                return false;
            }

            session = session with { Metadata = metadata, UpdatedAtUtc = DateTimeOffset.UtcNow };
            await sessionStore.SaveAsync(workspaceRoot, session, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConversationSession> RequireSessionAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
        => await ResolveSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No session resolved. Start or attach a session first.");

    private async Task<ConversationSession?> ResolveSessionAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            return await sessionStore.GetByIdAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        }

        var attached = await sessionCoordinator.GetAttachedSessionIdAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(attached))
        {
            return await sessionStore.GetByIdAsync(workspaceRoot, attached, cancellationToken).ConfigureAwait(false);
        }

        return await sessionStore.GetLatestAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
    }

    private static PermissionStatusReport BuildReport(
        ConversationSession? session,
        string? attachedSessionId,
        PermissionMode fallbackPermissionMode,
        ApprovalSettings? approvalSettings,
        string? currentModel)
    {
        var permissionMode = fallbackPermissionMode;
        var effectiveApprovalSettings = approvalSettings;
        if (session?.Metadata?.TryGetValue(SharpClawWorkflowMetadataKeys.PreferredPermissionMode, out var storedMode) == true
            && Enum.TryParse<PermissionMode>(storedMode, ignoreCase: true, out var parsed))
        {
            permissionMode = parsed;
        }

        if (session?.Metadata is not null)
        {
            var scopes = ReadApprovalScopes(session.Metadata);
            var budget = ReadApprovalBudget(session.Metadata);
            if (scopes is not null || budget is not null)
            {
                effectiveApprovalSettings = ApprovalSettingsResolver.Normalize(new ApprovalSettings(scopes ?? [], budget));
            }
        }

        var trustedSources = new List<TrustedSourceEntry>();
        foreach (var name in ReadStringArray(session?.Metadata, SharpClawWorkflowMetadataKeys.TrustedPluginNamesJson))
        {
            trustedSources.Add(new TrustedSourceEntry(TrustedSourceKind.Plugin, name, session?.UpdatedAtUtc ?? DateTimeOffset.UtcNow));
        }

        foreach (var name in ReadStringArray(session?.Metadata, SharpClawWorkflowMetadataKeys.TrustedMcpServerNamesJson))
        {
            trustedSources.Add(new TrustedSourceEntry(TrustedSourceKind.Mcp, name, session?.UpdatedAtUtc ?? DateTimeOffset.UtcNow));
        }

        var effectiveModel = currentModel;
        if (session?.Metadata?.TryGetValue(SharpClawWorkflowMetadataKeys.SessionModelPreferenceJson, out var payload) == true)
        {
            try
            {
                effectiveModel = JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.SessionModelPreference)?.Model ?? currentModel;
            }
            catch (JsonException)
            {
                // Ignore malformed preference payload.
            }
        }

        return new PermissionStatusReport(permissionMode, effectiveApprovalSettings, trustedSources.ToArray(), attachedSessionId, effectiveModel);
    }

    private static IReadOnlyList<ApprovalScope>? ReadApprovalScopes(IReadOnlyDictionary<string, string> metadata)
    {
        if (!metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.ListApprovalScope);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadApprovalBudget(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget, out var payload)
           && int.TryParse(payload, out var parsed)
           && parsed > 0
            ? parsed
            : null;

    private static IReadOnlyList<string> ReadStringArray(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null
            || !metadata.TryGetValue(key, out var payload)
            || string.IsNullOrWhiteSpace(payload))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize(payload, ProtocolJsonContext.Default.StringArray) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
