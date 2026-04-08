using Microsoft.Extensions.Logging;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Mutations;

/// <summary>
/// Coordinates durable mutation sets, checkpoint linkage, and undo/redo application.
/// </summary>
public sealed class CheckpointMutationCoordinator(
    IMutationSetStore mutationSetStore,
    ISessionStore sessionStore,
    MutationWorkspaceApplier workspaceApplier,
    IRuntimeEventPublisher eventPublisher,
    ILogger<CheckpointMutationCoordinator> logger)
{
    /// <summary>Metadata key marking the mutation set whose undo/redo apply failed partway through. While set, further undo/redo is refused.</summary>
    public const string PartialMutationKey = "undoRedoPartial";

    /// <summary>
    /// Persists a mutation set JSON payload and returns an updated session snapshot (caller performs durable session save).
    /// </summary>
    public async Task<ConversationSession> ApplyRecordedMutationsAsync(
        string workspacePath,
        ConversationSession session,
        string turnId,
        string checkpointId,
        IReadOnlyList<FileMutationOperation> operations,
        CancellationToken cancellationToken)
    {
        if (operations.Count == 0)
        {
            return session;
        }

        var md = new Dictionary<string, string>(session.Metadata ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var parse = UndoRedoStateHelper.Parse(md);
        if (parse.IsCorrupt)
        {
            throw new InvalidOperationException(
                $"Session undo/redo metadata is corrupt or unreadable; repair or clear '{SharpClawWorkflowMetadataKeys.UndoRedoStateJson}' before recording mutations. Detail: {parse.Detail}");
        }

        var state = parse.State;
        var history = state.MutationHistory.ToList();
        if (state.AppliedPrefixLength < history.Count)
        {
            history.RemoveRange(state.AppliedPrefixLength, history.Count - state.AppliedPrefixLength);
        }

        history.Add(checkpointId);
        var nextState = new UndoRedoStateDocument(history, history.Count, []);
        UndoRedoStateHelper.Assign(md, nextState);

        var updatedSession = session with
        {
            Metadata = md,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        var doc = new MutationSetDocument(
            SchemaVersion: "1.0",
            Id: checkpointId,
            SessionId: session.Id,
            TurnId: turnId,
            CheckpointId: checkpointId,
            RecordedAtUtc: DateTimeOffset.UtcNow,
            Operations: operations.ToArray());

        await mutationSetStore.SaveAsync(workspacePath, doc, cancellationToken).ConfigureAwait(false);
        await sessionStore.SaveAsync(workspacePath, updatedSession, cancellationToken).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new MutationSetRecordedEvent(
                EventId: CreateEventId(),
                SessionId: session.Id,
                TurnId: turnId,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                MutationSetId: checkpointId,
                CheckpointId: checkpointId,
                OperationCount: operations.Count),
            new RuntimeEventPublishOptions(
                workspacePath,
                session.Id,
                PersistToSessionStore: true,
                ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Recorded mutation set {MutationSetId} with {Count} operation(s) for session {SessionId}.",
            checkpointId,
            operations.Count,
            session.Id);

        return updatedSession;
    }

    /// <summary>
    /// Attempts to undo the last applied mutation set.
    /// </summary>
    public async Task<UndoRedoActionResult> TryUndoAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken,
        Func<Func<CancellationToken, Task<UndoRedoActionResult>>, Task<UndoRedoActionResult>>? executeWithinSessionLock = null)
    {
        if (executeWithinSessionLock is not null)
        {
            return await executeWithinSessionLock(ct => TryUndoAsync(workspacePath, sessionId, ct, null)).ConfigureAwait(false);
        }

        var session = await sessionStore.GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Fail("undo", "Session was not found.");
        }

        var md = new Dictionary<string, string>(session.Metadata ?? [], StringComparer.Ordinal);
        if (md.TryGetValue(PartialMutationKey, out var partial) && !string.IsNullOrEmpty(partial))
        {
            return Fail("undo", $"Mutation set '{partial}' is in a partial state from a prior failure; resolve manually before further undo/redo.");
        }

        var undoParse = UndoRedoStateHelper.Parse(md);
        if (undoParse.IsCorrupt)
        {
            logger.LogWarning(
                "Undo refused: corrupt undo/redo metadata for session {SessionId}. Detail: {Detail}",
                session.Id,
                undoParse.Detail);
            return Fail(
                "undo",
                $"Undo/redo metadata is corrupt or unreadable; repair or clear '{SharpClawWorkflowMetadataKeys.UndoRedoStateJson}' before undo. Detail: {undoParse.Detail}");
        }

        var state = undoParse.State;
        if (state.AppliedPrefixLength <= 0)
        {
            return Fail("undo", "Nothing to undo for this session.");
        }

        var mutationId = state.MutationHistory[state.AppliedPrefixLength - 1];
        var doc = await mutationSetStore.GetAsync(workspacePath, session.Id, mutationId, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return Fail("undo", $"Mutation set '{mutationId}' is missing from disk; cannot undo safely.");
        }

        await eventPublisher.PublishAsync(
            new UndoRequestedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, mutationId),
            new RuntimeEventPublishOptions(
                workspacePath,
                session.Id,
                PersistToSessionStore: true,
                ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);

        var appliedInverseCount = 0;
        try
        {
            for (var i = doc.Operations.Count - 1; i >= 0; i--)
            {
                await workspaceApplier
                    .ApplyInverseAsync(workspacePath, doc.Operations[i], cancellationToken)
                    .ConfigureAwait(false);
                appliedInverseCount++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Undo failed for session {SessionId}, set {MutationId} after {Applied} of {Total} ops.", session.Id, mutationId, appliedInverseCount, doc.Operations.Count);
            if (appliedInverseCount > 0 && appliedInverseCount < doc.Operations.Count)
            {
                md[PartialMutationKey] = mutationId;
                var partialSession = session with { Metadata = md, UpdatedAtUtc = DateTimeOffset.UtcNow };
                await sessionStore.SaveAsync(workspacePath, partialSession, CancellationToken.None).ConfigureAwait(false);
            }

            await eventPublisher.PublishAsync(
                new UndoFailedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, ex.Message),
                new RuntimeEventPublishOptions(
                    workspacePath,
                    session.Id,
                    PersistToSessionStore: true,
                    ThrowIfPersistenceFails: true),
                cancellationToken).ConfigureAwait(false);
            return new UndoRedoActionResult(false, "undo", mutationId, ex.Message, state.AppliedPrefixLength, state.RedoStack.Count);
        }

        var nextHistory = state.MutationHistory.ToList();
        var nextRedo = state.RedoStack.ToList();
        var appliedAfter = state.AppliedPrefixLength - 1;
        nextRedo.Add(mutationId);
        var nextState = new UndoRedoStateDocument(nextHistory, appliedAfter, nextRedo);
        UndoRedoStateHelper.Assign(md, nextState);

        var saved = session with
        {
            Metadata = md,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        await sessionStore.SaveAsync(workspacePath, saved, cancellationToken).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new UndoCompletedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, mutationId),
            new RuntimeEventPublishOptions(
                workspacePath,
                session.Id,
                PersistToSessionStore: true,
                ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);

        return new UndoRedoActionResult(
            true,
            "undo",
            mutationId,
            $"Undone mutation set '{mutationId}'.",
            nextState.AppliedPrefixLength,
            nextState.RedoStack.Count);
    }

    /// <summary>
    /// Attempts to redo the last undone mutation set.
    /// </summary>
    public async Task<UndoRedoActionResult> TryRedoAsync(
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken,
        Func<Func<CancellationToken, Task<UndoRedoActionResult>>, Task<UndoRedoActionResult>>? executeWithinSessionLock = null)
    {
        if (executeWithinSessionLock is not null)
        {
            return await executeWithinSessionLock(ct => TryRedoAsync(workspacePath, sessionId, ct, null)).ConfigureAwait(false);
        }

        var session = await sessionStore.GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Fail("redo", "Session was not found.");
        }

        var md = new Dictionary<string, string>(session.Metadata ?? [], StringComparer.Ordinal);
        if (md.TryGetValue(PartialMutationKey, out var partial) && !string.IsNullOrEmpty(partial))
        {
            return Fail("redo", $"Mutation set '{partial}' is in a partial state from a prior failure; resolve manually before further undo/redo.");
        }

        var redoParse = UndoRedoStateHelper.Parse(md);
        if (redoParse.IsCorrupt)
        {
            logger.LogWarning(
                "Redo refused: corrupt undo/redo metadata for session {SessionId}. Detail: {Detail}",
                session.Id,
                redoParse.Detail);
            return Fail(
                "redo",
                $"Undo/redo metadata is corrupt or unreadable; repair or clear '{SharpClawWorkflowMetadataKeys.UndoRedoStateJson}' before redo. Detail: {redoParse.Detail}");
        }

        var state = redoParse.State;
        if (state.RedoStack.Count == 0)
        {
            return Fail("redo", "Nothing to redo for this session.");
        }

        var mutationId = state.RedoStack[^1];
        if (state.AppliedPrefixLength >= state.MutationHistory.Count
            || !string.Equals(state.MutationHistory[state.AppliedPrefixLength], mutationId, StringComparison.Ordinal))
        {
            return Fail("redo", "Redo stack does not align with mutation history; refusing to replay.");
        }

        var doc = await mutationSetStore.GetAsync(workspacePath, session.Id, mutationId, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return Fail("redo", $"Mutation set '{mutationId}' is missing from disk; cannot redo safely.");
        }

        await eventPublisher.PublishAsync(
            new RedoRequestedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, mutationId),
            new RuntimeEventPublishOptions(
                workspacePath,
                session.Id,
                PersistToSessionStore: true,
                ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);

        var appliedForwardCount = 0;
        try
        {
            foreach (var op in doc.Operations)
            {
                await workspaceApplier.ApplyForwardAsync(workspacePath, op, cancellationToken).ConfigureAwait(false);
                appliedForwardCount++;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redo failed for session {SessionId}, set {MutationId} after {Applied} of {Total} ops.", session.Id, mutationId, appliedForwardCount, doc.Operations.Count);
            if (appliedForwardCount > 0 && appliedForwardCount < doc.Operations.Count)
            {
                md[PartialMutationKey] = mutationId;
                var partialSession = session with { Metadata = md, UpdatedAtUtc = DateTimeOffset.UtcNow };
                await sessionStore.SaveAsync(workspacePath, partialSession, CancellationToken.None).ConfigureAwait(false);
            }

            await eventPublisher.PublishAsync(
                new RedoFailedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, ex.Message),
                new RuntimeEventPublishOptions(
                    workspacePath,
                    session.Id,
                    PersistToSessionStore: true,
                    ThrowIfPersistenceFails: true),
                cancellationToken).ConfigureAwait(false);
            return new UndoRedoActionResult(false, "redo", mutationId, ex.Message, state.AppliedPrefixLength, state.RedoStack.Count);
        }

        var nextHistory = state.MutationHistory.ToList();
        var nextRedo = state.RedoStack.ToList();
        nextRedo.RemoveAt(nextRedo.Count - 1);
        var appliedAfter = state.AppliedPrefixLength + 1;
        var nextState = new UndoRedoStateDocument(nextHistory, appliedAfter, nextRedo);
        UndoRedoStateHelper.Assign(md, nextState);

        var saved = session with
        {
            Metadata = md,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        await sessionStore.SaveAsync(workspacePath, saved, cancellationToken).ConfigureAwait(false);

        await eventPublisher.PublishAsync(
            new RedoCompletedEvent(CreateEventId(), session.Id, null, DateTimeOffset.UtcNow, mutationId),
            new RuntimeEventPublishOptions(
                workspacePath,
                session.Id,
                PersistToSessionStore: true,
                ThrowIfPersistenceFails: true),
            cancellationToken).ConfigureAwait(false);

        return new UndoRedoActionResult(
            true,
            "redo",
            mutationId,
            $"Redone mutation set '{mutationId}'.",
            nextState.AppliedPrefixLength,
            nextState.RedoStack.Count);
    }

    /// <summary>
    /// Builds a summary for inspection surfaces.
    /// </summary>
    public static UndoRedoSnapshot ToSnapshot(ConversationSession session)
    {
        var parse = UndoRedoStateHelper.Parse(session.Metadata);
        var state = parse.State;
        var last = state.AppliedPrefixLength > 0
            ? state.MutationHistory[state.AppliedPrefixLength - 1]
            : null;
        return new UndoRedoSnapshot(
            state.MutationHistory.Count,
            state.AppliedPrefixLength,
            state.RedoStack.Count,
            last,
            parse.IsCorrupt);
    }

    private static UndoRedoActionResult Fail(string action, string message)
        => new(false, action, null, message, 0, 0);

    private static string CreateEventId() => $"event-{Guid.NewGuid():N}";
}
