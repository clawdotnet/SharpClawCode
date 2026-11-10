using System.Globalization;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Export;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using static SharpClaw.Code.Runtime.Orchestration.SessionMetadataHelpers;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Session administration commands: fork, export, portable bundles, attach/detach, list, share/unshare, compact, undo/redo.
/// Commands that mutate a session hold <see cref="SessionTurnLock"/> so they never interleave with a running turn.
/// <see cref="ConversationRuntime"/> exposes them through <see cref="IRuntimeCommandService"/> and <see cref="IConversationRuntime"/>.
/// </summary>
public sealed class SessionAdminCommands(
    ISessionStore sessionStore,
    IEventStore eventStore,
    IRuntimeEventPublisher eventPublisher,
    ISystemClock systemClock,
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    IRuntimeHostContextAccessor hostContextAccessor,
    ISessionExportService sessionExportService,
    IWorkspaceSessionAttachmentStore workspaceSessionAttachmentStore,
    CheckpointMutationCoordinator checkpointMutationCoordinator,
    ISessionCoordinator sessionCoordinator,
    IPortableSessionBundleService portableSessionBundleService,
    IShareSessionService shareSessionService,
    IConversationCompactionService conversationCompactionService,
    SessionTurnLock sessionTurnLock)
{
    /// <inheritdoc cref="IRuntimeCommandService.ForkSessionAsync(string?, RuntimeCommandContext, CancellationToken)"/>
    public async Task<CommandResult> ForkSessionAsync(
        string? sourceSessionId,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var child = await ForkSessionAsync(NormalizeWorkspacePath(context.WorkingDirectory), sourceSessionId, cancellationToken)
                .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(child, ProtocolJsonContext.Default.ConversationSession);
            return new CommandResult(
                true,
                0,
                context.OutputFormat,
                $"Forked session {child.Id} from '{child.Metadata?.GetValueOrDefault(SharpClawWorkflowMetadataKeys.ParentSessionId) ?? "?"}'.",
                payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.ExportSessionAsync"/>
    public async Task<CommandResult> ExportSessionAsync(
        string? sessionId,
        SessionExportFormat format,
        string? outputFilePath,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var (document, ext) = await sessionExportService
                .BuildDocumentAsync(workspace, sessionId, format, cancellationToken)
                .ConfigureAwait(false);

            var exportsDir = storagePathResolver.GetExportsRoot(workspace);
            fileSystem.CreateDirectory(exportsDir);
            var fileName =
                $"{document.Session.Id}-{document.ExportedAtUtc:yyyyMMddTHHmmss}.{ext}";
            var targetPath = string.IsNullOrWhiteSpace(outputFilePath)
                ? pathService.Combine(exportsDir, fileName)
                : pathService.GetFullPath(outputFilePath);

            var text = format == SessionExportFormat.Json
                ? JsonSerializer.Serialize(document, ProtocolJsonContext.Default.SessionExportDocument)
                : sessionExportService.RenderMarkdown(document);

            await fileSystem.WriteAllTextAsync(targetPath, text, cancellationToken).ConfigureAwait(false);

            return new CommandResult(
                true,
                0,
                context.OutputFormat,
                $"Exported session to '{targetPath}'.",
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["path"] = targetPath, ["format"] = ext },
                    ProtocolJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.UndoAsync"/>
    public async Task<CommandResult> UndoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var sid = await ResolveCommandSessionIdAsync(workspace, sessionId, context.SessionId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new CommandResult(false, 1, context.OutputFormat, "No session resolved for undo.", null);
            }

            var result = await checkpointMutationCoordinator
                .TryUndoAsync(
                    workspace,
                    sid,
                    cancellationToken,
                    operation => ExecuteWithSessionLockAsync(workspace, sid, operation, cancellationToken))
                .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.UndoRedoActionResult);
            return new CommandResult(result.Succeeded, result.Succeeded ? 0 : 1, context.OutputFormat, result.Message, payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.RedoAsync"/>
    public async Task<CommandResult> RedoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var sid = await ResolveCommandSessionIdAsync(workspace, sessionId, context.SessionId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new CommandResult(false, 1, context.OutputFormat, "No session resolved for redo.", null);
            }

            var result = await checkpointMutationCoordinator
                .TryRedoAsync(
                    workspace,
                    sid,
                    cancellationToken,
                    operation => ExecuteWithSessionLockAsync(workspace, sid, operation, cancellationToken))
                .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.UndoRedoActionResult);
            return new CommandResult(result.Succeeded, result.Succeeded ? 0 : 1, context.OutputFormat, result.Message, payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.ExportPortableSessionBundleAsync"/>
    public async Task<CommandResult> ExportPortableSessionBundleAsync(
        string? sessionId,
        string? outputZipPath,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var path = await portableSessionBundleService
                .CreateBundleZipAsync(workspace, sessionId, outputZipPath, cancellationToken)
                .ConfigureAwait(false);
            return new CommandResult(
                true,
                0,
                context.OutputFormat,
                $"Portable bundle written to '{path}'.",
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["path"] = path },
                    ProtocolJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.ImportPortableSessionBundleAsync"/>
    public async Task<CommandResult> ImportPortableSessionBundleAsync(
        string bundleZipPath,
        bool replaceExisting,
        bool attachAfterImport,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var zip = pathService.GetFullPath(bundleZipPath);
            var result = await portableSessionBundleService
                .ImportBundleZipAsync(workspace, zip, replaceExisting, cancellationToken)
                .ConfigureAwait(false);
            if (attachAfterImport)
            {
                await sessionCoordinator.AttachSessionAsync(workspace, result.SessionId, cancellationToken).ConfigureAwait(false);
            }

            var message = attachAfterImport
                ? $"Imported session '{result.SessionId}' and attached it for this workspace."
                : $"Imported session '{result.SessionId}'.";
            var payload = JsonSerializer.Serialize(result, ProtocolJsonContext.Default.PortableBundleImportResult);
            return new CommandResult(true, 0, context.OutputFormat, message, payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.ListSessionsAsync"/>
    public async Task<CommandResult> ListSessionsAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var rows = await sessionCoordinator.ListSessionsAsync(workspace, cancellationToken).ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(rows, ProtocolJsonContext.Default.ListSessionSummaryRow);
            return new CommandResult(true, 0, context.OutputFormat, $"{rows.Count} session(s).", payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.AttachSessionAsync"/>
    public async Task<CommandResult> AttachSessionAsync(string sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            await sessionCoordinator.AttachSessionAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
            return new CommandResult(
                true,
                0,
                context.OutputFormat,
                $"Attached session '{sessionId}' for workspace prompts.",
                JsonSerializer.Serialize(
                    new Dictionary<string, string> { ["sessionId"] = sessionId },
                    ProtocolJsonContext.Default.DictionaryStringString));
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.DetachSessionAsync"/>
    public async Task<CommandResult> DetachSessionAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            await sessionCoordinator.DetachSessionAsync(workspace, cancellationToken).ConfigureAwait(false);
            return new CommandResult(true, 0, context.OutputFormat, "Detached explicit workspace session.", null);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.ShareSessionAsync"/>
    public async Task<CommandResult> ShareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var sid = await ResolveCommandSessionIdAsync(workspace, sessionId, context.SessionId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new CommandResult(false, 1, context.OutputFormat, "No session resolved for sharing.", null);
            }

            var share = await ExecuteWithSessionLockAsync(
                    workspace,
                    sid,
                    ct => shareSessionService.CreateShareAsync(workspace, sid, ct),
                    cancellationToken)
                .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(share, ProtocolJsonContext.Default.ShareSessionRecord);
            return new CommandResult(true, 0, context.OutputFormat, $"Shared session '{sid}' at {share.Url}.", payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.UnshareSessionAsync"/>
    public async Task<CommandResult> UnshareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var sid = await ResolveCommandSessionIdAsync(workspace, sessionId, context.SessionId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new CommandResult(false, 1, context.OutputFormat, "No session resolved for unshare.", null);
            }

            var removed = await ExecuteWithSessionLockAsync(
                    workspace,
                    sid,
                    ct => shareSessionService.RemoveShareAsync(workspace, sid, ct),
                    cancellationToken)
                .ConfigureAwait(false);
            return new CommandResult(
                removed,
                removed ? 0 : 1,
                context.OutputFormat,
                removed ? $"Removed share for session '{sid}'." : $"Session '{sid}' is not currently shared.",
                null);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IRuntimeCommandService.CompactSessionAsync"/>
    public async Task<CommandResult> CompactSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        try
        {
            var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
            var sid = await ResolveCommandSessionIdAsync(workspace, sessionId, context.SessionId, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new CommandResult(false, 1, context.OutputFormat, "No session resolved for compaction.", null);
            }

            var result = await ExecuteWithSessionLockAsync(
                    workspace,
                    sid,
                    ct => conversationCompactionService.CompactAsync(workspace, sid, ct),
                    cancellationToken)
                .ConfigureAwait(false);
            var payload = JsonSerializer.Serialize(result.Session, ProtocolJsonContext.Default.ConversationSession);
            return new CommandResult(true, 0, context.OutputFormat, result.Summary, payload);
        }
        catch (Exception ex)
        {
            return new CommandResult(false, 1, context.OutputFormat, ex.Message, null);
        }
    }

    /// <inheritdoc cref="IConversationRuntime.ForkSessionAsync"/>
    public async Task<ConversationSession> ForkSessionAsync(string workspacePath, string? sourceSessionId, CancellationToken cancellationToken)
    {
        var normalized = NormalizeWorkspacePath(workspacePath);
        var parent = string.IsNullOrWhiteSpace(sourceSessionId)
            ? await sessionStore.GetLatestAsync(normalized, cancellationToken).ConfigureAwait(false)
            : await sessionStore.GetByIdAsync(normalized, sourceSessionId, cancellationToken).ConfigureAwait(false);
        if (parent is null)
        {
            throw new InvalidOperationException("Source session was not found.");
        }

        var events = await eventStore.ReadAllAsync(normalized, parent.Id, cancellationToken).ConfigureAwait(false);
        var summary = BuildForkHistorySummary(events);

        fileSystem.CreateDirectory(storagePathResolver.GetSharpClawRoot(normalized));

        var childId = CreateIdentifier("session");
        var md = CloneMetadata(parent.Metadata);
        md.Remove(SharpClawWorkflowMetadataKeys.UndoRedoStateJson);
        md.Remove(CheckpointMutationCoordinator.PartialMutationKey);
        md[SharpClawWorkflowMetadataKeys.ParentSessionId] = parent.Id;
        md[SharpClawWorkflowMetadataKeys.ForkedAtUtc] = systemClock.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        md[SharpClawWorkflowMetadataKeys.ForkHistorySummary] = summary;
        md[LastTurnSequenceKey] = "0";

        var child = new ConversationSession(
            Id: childId,
            Title: $"Fork of {parent.Id[..Math.Min(8, parent.Id.Length)]}",
            State: SessionLifecycleState.Created,
            PermissionMode: parent.PermissionMode,
            OutputFormat: parent.OutputFormat,
            WorkingDirectory: normalized,
            RepositoryRoot: normalized,
            CreatedAtUtc: systemClock.UtcNow,
            UpdatedAtUtc: systemClock.UtcNow,
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: md);

        await sessionStore.SaveAsync(normalized, child, cancellationToken).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new SessionForkedEvent(
                EventId: CreateIdentifier("event"),
                SessionId: childId,
                TurnId: null,
                OccurredAtUtc: systemClock.UtcNow,
                ParentSessionId: parent.Id,
                ChildSessionId: childId,
                ForkedFromCheckpointId: parent.LastCheckpointId),
            new RuntimeEventPublishOptions(normalized, childId, PersistToSessionStore: true),
            cancellationToken).ConfigureAwait(false);

        return child;
    }

    private static string BuildForkHistorySummary(IReadOnlyList<RuntimeEvent> events)
    {
        var completedTurns = events.OfType<TurnCompletedEvent>().Count();
        return completedTurns == 0
            ? "Forked from parent session (no completed turns in persisted event log)."
            : $"Forked from parent session with {completedTurns} completed turn(s) in the persisted log.";
    }

    private async Task<string?> ResolveCommandSessionIdAsync(
        string workspacePath,
        string? explicitSessionId,
        string? contextSessionId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(explicitSessionId))
        {
            return explicitSessionId;
        }

        if (!string.IsNullOrWhiteSpace(contextSessionId))
        {
            return contextSessionId;
        }

        var attached = await workspaceSessionAttachmentStore
            .GetAttachedSessionIdAsync(workspacePath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(attached))
        {
            return attached;
        }

        var latest = await sessionStore.GetLatestAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return latest?.Id;
    }

    private string NormalizeWorkspacePath(string? workspacePath)
        => pathService.GetFullPath(string.IsNullOrWhiteSpace(workspacePath) ? pathService.GetCurrentDirectory() : workspacePath);

    private Task<T> ExecuteWithSessionLockAsync<T>(
        string workspacePath,
        string sessionId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
        => sessionTurnLock.ExecuteAsync(workspacePath, sessionId, operation, cancellationToken);
}
