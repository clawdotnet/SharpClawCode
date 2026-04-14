using System.Globalization;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Stores self-hosted share snapshots under the workspace .sharpclaw directory.
/// </summary>
public sealed class ShareSessionService(
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock,
    ISessionStore sessionStore,
    IEventStore eventStore,
    ISharpClawConfigService configService,
    IRuntimeEventPublisher eventPublisher,
    IHookDispatcher hookDispatcher) : IShareSessionService
{
    /// <inheritdoc />
    public async Task<ShareSessionRecord> CreateShareAsync(string workspaceRoot, string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var session = await sessionStore.GetByIdAsync(normalizedWorkspace, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var config = await configService.GetConfigAsync(normalizedWorkspace, cancellationToken).ConfigureAwait(false);
        if ((config.Document.ShareMode ?? ShareMode.Manual) == ShareMode.Disabled)
        {
            throw new InvalidOperationException("Sharing is disabled for this workspace.");
        }

        var shareId = session.Metadata is not null
            && session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ShareId, out var existingShareId)
            && !string.IsNullOrWhiteSpace(existingShareId)
                ? existingShareId
                : $"share-{Guid.NewGuid():N}";

        var url = BuildShareUrl(config, shareId);
        var record = new ShareSessionRecord(
            shareId,
            sessionId,
            normalizedWorkspace,
            url,
            config.Document.ShareMode ?? ShareMode.Manual,
            systemClock.UtcNow);

        var events = await eventStore.ReadAllAsync(normalizedWorkspace, sessionId, cancellationToken).ConfigureAwait(false);
        var snapshot = new SharedSessionSnapshot(record, SanitizeSession(session, record), events.ToArray());
        var snapshotPath = SessionStorageLayout.GetShareSnapshotPath(pathService, normalizedWorkspace, shareId);
        fileSystem.CreateDirectory(SessionStorageLayout.GetSharesRoot(pathService, normalizedWorkspace));
        await fileSystem
            .WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, ProtocolJsonContext.Default.SharedSessionSnapshot), cancellationToken)
            .ConfigureAwait(false);

        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata[SharpClawWorkflowMetadataKeys.ShareId] = shareId;
        metadata[SharpClawWorkflowMetadataKeys.ShareUrl] = url;
        metadata[SharpClawWorkflowMetadataKeys.SharedAtUtc] = record.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture);
        session = session with
        {
            UpdatedAtUtc = systemClock.UtcNow,
            Metadata = metadata,
        };

        await sessionStore.SaveAsync(normalizedWorkspace, session, cancellationToken).ConfigureAwait(false);
        var shareCreatedEvent = new ShareCreatedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: session.Id,
            TurnId: session.ActiveTurnId,
            OccurredAtUtc: record.CreatedAtUtc,
            Share: record);
        await eventPublisher.PublishAsync(
            shareCreatedEvent,
            new RuntimeEventPublishOptions(normalizedWorkspace, session.Id, PersistToSessionStore: true, ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);
        await hookDispatcher
            .DispatchAsync(
                normalizedWorkspace,
                HookTriggerKind.ShareCreated,
                JsonSerializer.Serialize(shareCreatedEvent, ProtocolJsonContext.Default.ShareCreatedEvent),
                cancellationToken)
            .ConfigureAwait(false);
        return record;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveShareAsync(string workspaceRoot, string sessionId, CancellationToken cancellationToken)
    {
        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var session = await sessionStore.GetByIdAsync(normalizedWorkspace, sessionId, cancellationToken).ConfigureAwait(false);
        if (session?.Metadata is null
            || !session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ShareId, out var shareId)
            || string.IsNullOrWhiteSpace(shareId))
        {
            return false;
        }

        fileSystem.TryDeleteFile(SessionStorageLayout.GetShareSnapshotPath(pathService, normalizedWorkspace, shareId));
        var metadata = new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata.Remove(SharpClawWorkflowMetadataKeys.ShareId);
        metadata.Remove(SharpClawWorkflowMetadataKeys.ShareUrl);
        metadata.Remove(SharpClawWorkflowMetadataKeys.SharedAtUtc);

        var updated = session with
        {
            UpdatedAtUtc = systemClock.UtcNow,
            Metadata = metadata,
        };

        await sessionStore.SaveAsync(normalizedWorkspace, updated, cancellationToken).ConfigureAwait(false);
        var shareRemovedEvent = new ShareRemovedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: session.Id,
            TurnId: session.ActiveTurnId,
            OccurredAtUtc: systemClock.UtcNow,
            ShareId: shareId);
        await eventPublisher.PublishAsync(
            shareRemovedEvent,
            new RuntimeEventPublishOptions(normalizedWorkspace, session.Id, PersistToSessionStore: true, ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);
        await hookDispatcher
            .DispatchAsync(
                normalizedWorkspace,
                HookTriggerKind.ShareRemoved,
                JsonSerializer.Serialize(shareRemovedEvent, ProtocolJsonContext.Default.ShareRemovedEvent),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<SharedSessionSnapshot?> GetSharedSnapshotAsync(string workspaceRoot, string shareId, CancellationToken cancellationToken)
    {
        var content = await fileSystem
            .ReadAllTextIfExistsAsync(SessionStorageLayout.GetShareSnapshotPath(pathService, pathService.GetFullPath(workspaceRoot), shareId), cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content)
            ? null
            : JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.SharedSessionSnapshot);
    }

    private static string BuildShareUrl(SharpClawConfigSnapshot config, string shareId)
    {
        var server = config.Document.Server ?? new SharpClawServerOptions("127.0.0.1", 7345, null);
        var baseUrl = string.IsNullOrWhiteSpace(server.PublicBaseUrl)
            ? $"http://{server.Host}:{server.Port}"
            : server.PublicBaseUrl!.TrimEnd('/');
        return $"{baseUrl.TrimEnd('/')}/s/{Uri.EscapeDataString(shareId)}";
    }

    private static ConversationSession SanitizeSession(ConversationSession session, ShareSessionRecord record)
    {
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata[SharpClawWorkflowMetadataKeys.ShareId] = record.ShareId;
        metadata[SharpClawWorkflowMetadataKeys.ShareUrl] = record.Url;
        metadata.Remove(SharpClawWorkflowMetadataKeys.EditorContextJson);
        return session with { Metadata = metadata };
    }
}
