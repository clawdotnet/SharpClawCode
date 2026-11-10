using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.CustomCommands;
using SharpClaw.Code.Runtime.Diagnostics;
using SharpClaw.Code.Runtime.Export;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Runtime.Lifecycle;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

using static SharpClaw.Code.Runtime.Orchestration.SessionMetadataHelpers;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Implements durable conversation runtime orchestration for prompt execution.
/// </summary>
public sealed class ConversationRuntime(
    ISessionStore sessionStore,
    IRuntimeEventPublisher eventPublisher,
    ICheckpointStore checkpointStore,
    ITurnRunner turnRunner,
    IRuntimeStateMachine stateMachine,
    ISystemClock systemClock,
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    IRuntimeHostContextAccessor hostContextAccessor,
    ICustomCommandDiscoveryService customCommandDiscovery,
    IWorkspaceSessionAttachmentStore workspaceSessionAttachmentStore,
    IEditorContextBuffer editorContextBuffer,
    CheckpointMutationCoordinator checkpointMutationCoordinator,
    IPlanWorkflowService planWorkflowService,
    ISpecWorkflowService specWorkflowService,
    ISharpClawConfigService sharpClawConfigService,
    IAgentCatalogService agentCatalogService,
    IShareSessionService shareSessionService,
    IHookDispatcher hookDispatcher,
    SessionTurnLock sessionTurnLock,
    RuntimeDiagnosticsCommands diagnosticsCommands,
    SessionAdminCommands sessionAdminCommands,
    ILogger<ConversationRuntime> logger,
    TurnMutationJournal? mutationJournal = null,
    IMutationSetStore? mutationSets = null) : IConversationRuntime, IRuntimeCommandService
{
    private const string CanceledTurnReason = "The turn was canceled.";
    private const string FailedTurnReason = "The turn failed.";

    /// <inheritdoc />
    public async Task<ConversationSession> CreateSessionAsync(string workspacePath, PermissionMode permissionMode, OutputFormat outputFormat, CancellationToken cancellationToken)
    {
        var normalizedWorkspacePath = NormalizeWorkspacePath(workspacePath);
        fileSystem.CreateDirectory(storagePathResolver.GetSharpClawRoot(normalizedWorkspacePath));

        var sessionId = CreateIdentifier("session");
        var session = new ConversationSession(
            Id: sessionId,
            Title: $"Session {sessionId[..8]}",
            State: SessionLifecycleState.Created,
            PermissionMode: permissionMode,
            OutputFormat: outputFormat,
            WorkingDirectory: normalizedWorkspacePath,
            RepositoryRoot: normalizedWorkspacePath,
            CreatedAtUtc: systemClock.UtcNow,
            UpdatedAtUtc: systemClock.UtcNow,
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: new Dictionary<string, string>
            {
                [LastTurnSequenceKey] = "0"
            });

        await sessionStore.SaveAsync(normalizedWorkspacePath, session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    /// <inheritdoc />
    public Task<ConversationSession?> GetSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
        => sessionStore.GetByIdAsync(NormalizeWorkspacePath(workspacePath), sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationSession?> GetLatestSessionAsync(string workspacePath, CancellationToken cancellationToken)
        => sessionStore.GetLatestAsync(NormalizeWorkspacePath(workspacePath), cancellationToken);

    /// <inheritdoc />
    public async Task<TurnExecutionResult> RunPromptAsync(RunPromptRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        using var hostScope = hostContextAccessor.BeginScope(request.HostContext);

        var workspacePath = NormalizeWorkspacePath(request.WorkingDirectory);
        request = EnrichRequestWithEditorIngress(workspacePath, request);
        var explicitPrimaryMode = request.PrimaryMode;
        request = await ApplyAgentAndConfigDefaultsAsync(workspacePath, request, cancellationToken).ConfigureAwait(false);
        var runtimeEvents = new List<RuntimeEvent>();
        var session = await ResolveSessionAsync(workspacePath, request.SessionId, cancellationToken).ConfigureAwait(false);
        var isNewSession = false;

        if (session is null)
        {
            session = await CreateSessionAsync(workspacePath, request.PermissionMode, request.OutputFormat, cancellationToken).ConfigureAwait(false);
            isNewSession = true;
        }

        if (request.SessionId is not null && !string.Equals(session.Id, request.SessionId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unable to resume session '{request.SessionId}'.");
        }

        await using var sessionLock = await sessionTurnLock.AcquireAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);

        // Re-read the session inside the lock so the turn sequence read-modify-write is consistent across concurrent prompts.
        var refreshed = await sessionStore.GetByIdAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);
        if (refreshed is not null)
        {
            session = refreshed;
        }

        if (explicitPrimaryMode is null && session.Metadata is not null
            && session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.PrimaryMode, out var savedMode)
            && Enum.TryParse<PrimaryMode>(savedMode, true, out var persistedMode) && Enum.IsDefined(persistedMode))
            request = request with { PrimaryMode = persistedMode };
        session = await EnsurePrimaryModePersistedAsync(workspacePath, session, request.PrimaryMode, cancellationToken).ConfigureAwait(false);
        session = await EnsureApprovalSettingsPersistedAsync(workspacePath, session, request.ApprovalSettings, cancellationToken).ConfigureAwait(false);

        session = await EnsureRecoverableFromFailedForPromptAsync(
                workspacePath,
                session,
                runtimeEvents,
                cancellationToken)
            .ConfigureAwait(false);

        if (isNewSession)
        {
            await AppendEventAsync(
                workspacePath,
                session.Id,
                new SessionCreatedEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: session.Id,
                    TurnId: null,
                    OccurredAtUtc: systemClock.UtcNow,
                    Session: session),
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);
        }

        var previousState = session.State;
        var activeState = stateMachine.Transition(session.State, RuntimeLifecycleTransition.Activate);
        if (previousState != activeState)
        {
            session = session with
            {
                State = activeState,
                UpdatedAtUtc = systemClock.UtcNow,
            };

            await AppendEventAsync(
                workspacePath,
                session.Id,
                new SessionStateChangedEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: session.Id,
                    TurnId: null,
                    OccurredAtUtc: systemClock.UtcNow,
                    PreviousState: previousState,
                    CurrentState: activeState,
                    Reason: "Prompt execution activated the session."),
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);
        }

        var turnSequenceNumber = GetLastTurnSequence(session) + 1;
        var turnId = CreateIdentifier("turn");
        var startedAtUtc = systemClock.UtcNow;
        var primaryAgentId = string.IsNullOrWhiteSpace(request.AgentId) ? "primary-coding-agent" : request.AgentId!;
        var turn = new ConversationTurn(
            Id: turnId,
            SessionId: session.Id,
            SequenceNumber: turnSequenceNumber,
            Input: request.Prompt,
            Output: null,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: null,
            AgentId: primaryAgentId,
            SlashCommandName: null,
            Usage: null,
            Metadata: new Dictionary<string, string>
            {
                ["workspacePath"] = workspacePath,
                ["resumeMode"] = request.SessionId is null ? "latest-or-create" : "explicit"
            });

        var metadataAtTurnStart = CloneMetadata(session.Metadata);
        metadataAtTurnStart[LastTurnSequenceKey] = turnSequenceNumber.ToString(CultureInfo.InvariantCulture);
        metadataAtTurnStart["lastTurnId"] = turnId;
        if (!string.IsNullOrWhiteSpace(request.AgentId))
        {
            metadataAtTurnStart[SharpClawWorkflowMetadataKeys.ActiveAgentId] = request.AgentId!;
        }
        session = session with
        {
            ActiveTurnId = turnId,
            UpdatedAtUtc = startedAtUtc,
            Metadata = metadataAtTurnStart,
        };

        // Persist sequence allocation before any cancellable I/O so failed/canceled turns
        // still monotonically consume sequence numbers even if the event append is cancelled.
        await sessionStore.SaveAsync(workspacePath, session, CancellationToken.None).ConfigureAwait(false);

        await AppendEventAsync(
            workspacePath,
            session.Id,
            new TurnStartedEvent(
                EventId: CreateIdentifier("event"),
                SessionId: session.Id,
                TurnId: turnId,
                OccurredAtUtc: startedAtUtc,
                Turn: turn),
            runtimeEvents,
            cancellationToken).ConfigureAwait(false);

        var capturedMutations = mutationJournal?.Begin(session.Id, turnId);
        IReadOnlyList<FileMutationOperation>? completedMutations = null;
        string? mutationCheckpointId = null;
        var mutationsPersisted = false;
        try
        {
            var effectivePrimary = PrimaryModeResolver.ResolveEffective(request, session);
            var effectiveApprovalSettings = ApprovalSettingsResolver.ResolveEffective(request, session);
            var runnerRequest = request with
            {
                WorkingDirectory = workspacePath,
                Metadata = MergeMetadata(request.Metadata, request.PermissionMode, request.OutputFormat, effectivePrimary, effectiveApprovalSettings),
                ApprovalSettings = effectiveApprovalSettings
            };

            var turnRunResult = await turnRunner.RunAsync(session, turn, runnerRequest, cancellationToken).ConfigureAwait(false);
            completedMutations = turnRunResult.FileMutations;
            var verificationFailed = turnRunResult.Verification?.Status is VerificationStatus.Failed or VerificationStatus.Cancelled;
            SpecArtifactSet? specArtifacts = null;
            PlanExecutionResult? planResult = null;
            if (effectivePrimary == PrimaryMode.Plan)
            {
                planResult = await planWorkflowService
                    .MaterializeAsync(workspacePath, session.Id, request.Prompt, turnRunResult.Output, cancellationToken)
                    .ConfigureAwait(false);
                session = await sessionStore.GetByIdAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false) ?? session;

                turnRunResult = turnRunResult with
                {
                    Output = planWorkflowService.RenderCompletionMessage(planResult),
                    Summary = planResult.Summary
                };
            }
            else if (effectivePrimary == PrimaryMode.Spec)
            {
                specArtifacts = await specWorkflowService
                    .MaterializeAsync(workspacePath, request.Prompt, turnRunResult.Output, cancellationToken)
                    .ConfigureAwait(false);

                turnRunResult = turnRunResult with
                {
                    Output = FormatSpecCompletionMessage(specArtifacts),
                    Summary = $"Generated spec artifacts in '{specArtifacts.RootPath}'."
                };
            }

            var completedAtUtc = systemClock.UtcNow;
            var completedTurn = turn with
            {
                Output = turnRunResult.Output,
                CompletedAtUtc = completedAtUtc,
                Usage = turnRunResult.Usage,
                Verification = turnRunResult.Verification,
            };
            ConversationHistoryCache.StoreCompletedTurn(workspacePath, session.Id, completedTurn);

            await AppendRuntimeEventsAsync(
                workspacePath,
                session.Id,
                turnRunResult.RuntimeEvents,
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);

            runtimeEvents.AddRange(turnRunResult.PersistedRuntimeEvents ?? []);
            await AppendProviderEventsAsync(
                workspacePath,
                session.Id,
                turnId,
                turnRunResult,
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);

            await AppendEventAsync(
                workspacePath,
                session.Id,
                new UsageUpdatedEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: session.Id,
                    TurnId: turnId,
                    OccurredAtUtc: completedAtUtc,
                    Usage: turnRunResult.Usage),
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);

            var checkpointId = CreateIdentifier("checkpoint");
            mutationCheckpointId = checkpointId;
            var checkpoint = new RuntimeCheckpoint(
                Id: checkpointId,
                SessionId: session.Id,
                TurnId: turnId,
                CreatedAtUtc: completedAtUtc,
                Summary: turnRunResult.Summary,
                StateLocation: pathService.Combine(".sharpclaw", "sessions", session.Id, "checkpoints", $"{checkpointId}.json"),
                RecoveryHint: "Resume the latest session to continue the conversation.",
                Metadata: new Dictionary<string, string>
                {
                    ["turnSequence"] = turnSequenceNumber.ToString(CultureInfo.InvariantCulture)
                });

            await checkpointStore.SaveAsync(workspacePath, checkpoint, cancellationToken).ConfigureAwait(false);

            var metadata = CloneMetadata(session.Metadata);
            metadata[LastTurnSequenceKey] = turnSequenceNumber.ToString(CultureInfo.InvariantCulture);
            metadata["lastTurnId"] = turnId;
            if (!string.IsNullOrWhiteSpace(request.AgentId))
            {
                metadata[SharpClawWorkflowMetadataKeys.ActiveAgentId] = request.AgentId!;
            }

            session = session with
            {
                State = verificationFailed ? stateMachine.Transition(activeState, RuntimeLifecycleTransition.Fail) : activeState,
                UpdatedAtUtc = completedAtUtc,
                ActiveTurnId = null,
                LastCheckpointId = checkpoint.Id,
                Metadata = metadata,
            };

            if (turnRunResult.FileMutations is { Count: > 0 } mutations)
            {
                session = await checkpointMutationCoordinator
                    .ApplyRecordedMutationsAsync(
                        workspacePath,
                        session,
                        turnId,
                        checkpointId,
                        mutations,
                        cancellationToken)
                    .ConfigureAwait(false);
                mutationsPersisted = true;
            }
            if (verificationFailed)
            {
                await AppendEventAsync(workspacePath, session.Id,
                    new SessionStateChangedEvent(CreateIdentifier("event"), session.Id, turnId, completedAtUtc, activeState, session.State, turnRunResult.Verification!.Summary),
                    runtimeEvents, cancellationToken).ConfigureAwait(false);
            }

            await AppendEventAsync(
                workspacePath,
                session.Id,
                new TurnCompletedEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: session.Id,
                    TurnId: turnId,
                    OccurredAtUtc: completedAtUtc,
                    Turn: completedTurn,
                    Succeeded: !verificationFailed,
                    Summary: turnRunResult.Summary),
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);

            await sessionStore.SaveAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Completed prompt turn {TurnId} for session {SessionId}.", turnId, session.Id);

            SpecArtifactSet? finalSpecArtifacts = specArtifacts;
            if (await ShouldAutoShareAsync(workspacePath, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    var share = await shareSessionService.CreateShareAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);
                    session = await sessionStore.GetByIdAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false) ?? session;
                    finalSpecArtifacts = specArtifacts;
                    runtimeEvents.Add(
                        new ShareCreatedEvent(
                            EventId: CreateIdentifier("event"),
                            SessionId: session.Id,
                            TurnId: turnId,
                            OccurredAtUtc: share.CreatedAtUtc,
                            Share: share));
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Auto-share failed for session {SessionId}.", session.Id);
                }
            }

            return new TurnExecutionResult(
                Session: session,
                Turn: completedTurn,
                FinalOutput: completedTurn.Output,
                ToolResults: (turnRunResult.ToolResults ?? []).ToArray(),
                Usage: turnRunResult.Usage,
                Checkpoint: checkpoint,
                Events: runtimeEvents.ToArray(),
                SpecArtifacts: finalSpecArtifacts,
                PlanResult: planResult,
                Verification: turnRunResult.Verification);
        }
        catch (OperationCanceledException exception)
        {
            if (!mutationsPersisted) session = await PreserveInterruptedMutationsAsync(workspacePath, session, turnId, completedMutations ?? capturedMutations?.ToSnapshot() ?? [], mutationCheckpointId).ConfigureAwait(false);
            session = await PersistTurnFailureAsync(
                workspacePath,
                session,
                turnId,
                runtimeEvents,
                CanceledTurnReason,
                CancellationToken.None).ConfigureAwait(false);
            logger.LogWarning(
                exception,
                "Prompt execution was canceled for session {SessionId}, turn {TurnId}.",
                session.Id,
                turnId);
            throw;
        }
        catch (ProviderExecutionException exception)
        {
            if (!mutationsPersisted) session = await PreserveInterruptedMutationsAsync(workspacePath, session, turnId, completedMutations ?? capturedMutations?.ToSnapshot() ?? [], mutationCheckpointId).ConfigureAwait(false);
            session = await PersistTurnFailureAsync(
                workspacePath,
                session,
                turnId,
                runtimeEvents,
                FormatProviderFailureReason(exception),
                CancellationToken.None).ConfigureAwait(false);
            logger.LogError(
                exception,
                "Prompt execution failed due to provider error {FailureKind} for session {SessionId}, turn {TurnId}.",
                exception.Kind,
                session.Id,
                turnId);
            throw;
        }
        catch (Exception exception)
        {
            if (!mutationsPersisted) session = await PreserveInterruptedMutationsAsync(workspacePath, session, turnId, completedMutations ?? capturedMutations?.ToSnapshot() ?? [], mutationCheckpointId).ConfigureAwait(false);
            session = await PersistTurnFailureAsync(
                workspacePath,
                session,
                turnId,
                runtimeEvents,
                string.IsNullOrWhiteSpace(exception.Message) ? FailedTurnReason : exception.Message,
                CancellationToken.None).ConfigureAwait(false);
            logger.LogError(
                exception,
                "Prompt execution failed for session {SessionId}, turn {TurnId}.",
                session.Id,
                turnId);
            throw;
        }
        finally
        {
            mutationJournal?.End(session.Id, turnId);
        }
    }

    /// <inheritdoc />
    public Task<TurnExecutionResult> ExecutePromptAsync(string prompt, RuntimeCommandContext context, CancellationToken cancellationToken)
        => RunPromptAsync(
            new RunPromptRequest(
                Prompt: prompt,
                SessionId: context.SessionId,
                WorkingDirectory: context.WorkingDirectory,
                PermissionMode: context.PermissionMode,
                OutputFormat: context.OutputFormat,
                Metadata: new Dictionary<string, string?>
                {
                    ["model"] = context.Model
                }
                .Where(pair => pair.Value is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value!),
                PrimaryMode: context.PrimaryMode,
                AgentId: context.AgentId,
                IsInteractive: context.IsInteractive,
                HostContext: context.HostContext,
                ApprovalSettings: context.ApprovalSettings),
            cancellationToken);

    /// <inheritdoc />
    public async Task<TurnExecutionResult> ExecuteCustomCommandAsync(
        string commandName,
        string arguments,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var workspace = NormalizeWorkspacePath(context.WorkingDirectory);
        var definition = await customCommandDiscovery
            .FindAsync(workspace, commandName, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Unknown custom command '{commandName}'.");

        var expanded = CustomCommandTemplateExpander.Expand(definition.TemplateBody, arguments.Trim());
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(context.Model))
        {
            metadata["model"] = context.Model!;
        }

        if (!string.IsNullOrWhiteSpace(definition.Model))
        {
            metadata["model"] = definition.Model;
        }

        metadata[SharpClawWorkflowMetadataKeys.CustomCommandName] = definition.Name;

        var permission = ClampPermissionModeForCustomCommand(context.PermissionMode, definition.PermissionMode);
        // With no caller-selected mode and no command override, leave the mode unspecified so the session's saved mode applies.
        // Otherwise clamp against the caller's mode, falling back to the saved session mode, so markdown cannot leave Plan/Research/Spec.
        PrimaryMode? primary = null;
        if (context.PrimaryMode is not null || definition.PrimaryModeOverride is not null)
        {
            var baseline = context.PrimaryMode
                ?? await ResolveSavedPrimaryModeAsync(workspace, context.SessionId, cancellationToken).ConfigureAwait(false)
                ?? PrimaryMode.Build;
            primary = ClampPrimaryModeForCustomCommand(baseline, definition.PrimaryModeOverride);
        }

        return await RunPromptAsync(
            new RunPromptRequest(
                Prompt: expanded,
                SessionId: context.SessionId,
                WorkingDirectory: workspace,
                PermissionMode: permission,
                OutputFormat: context.OutputFormat,
                Metadata: metadata,
                PrimaryMode: primary,
                AgentId: definition.AgentId,
                IsInteractive: context.IsInteractive,
                HostContext: context.HostContext,
                ApprovalSettings: context.ApprovalSettings),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
        => diagnosticsCommands.GetStatusAsync(context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
        => diagnosticsCommands.RunDoctorAsync(context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> InspectSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => diagnosticsCommands.InspectSessionAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ForkSessionAsync(string? sourceSessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ForkSessionAsync(sourceSessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ExportSessionAsync(string? sessionId, SessionExportFormat format, string? outputFilePath, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ExportSessionAsync(sessionId, format, outputFilePath, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> UndoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.UndoAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> RedoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.RedoAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ExportPortableSessionBundleAsync(string? sessionId, string? outputZipPath, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ExportPortableSessionBundleAsync(sessionId, outputZipPath, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ImportPortableSessionBundleAsync(string bundleZipPath, bool replaceExisting, bool attachAfterImport, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ImportPortableSessionBundleAsync(bundleZipPath, replaceExisting, attachAfterImport, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ListSessionsAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ListSessionsAsync(context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> AttachSessionAsync(string sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.AttachSessionAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> DetachSessionAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.DetachSessionAsync(context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> ShareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.ShareSessionAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> UnshareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.UnshareSessionAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<CommandResult> CompactSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
        => sessionAdminCommands.CompactSessionAsync(sessionId, context, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationSession> ForkSessionAsync(string workspacePath, string? sourceSessionId, CancellationToken cancellationToken)
        => sessionAdminCommands.ForkSessionAsync(workspacePath, sourceSessionId, cancellationToken);

    private async Task<ConversationSession> EnsurePrimaryModePersistedAsync(
        string workspacePath,
        ConversationSession session,
        PrimaryMode? mode,
        CancellationToken cancellationToken)
    {
        if (mode is null)
        {
            return session;
        }

        var md = CloneMetadata(session.Metadata);
        var text = mode.Value.ToString();
        if (md.TryGetValue(SharpClawWorkflowMetadataKeys.PrimaryMode, out var current)
            && string.Equals(current, text, StringComparison.OrdinalIgnoreCase))
        {
            return session;
        }

        md[SharpClawWorkflowMetadataKeys.PrimaryMode] = text;
        var updated = session with
        {
            Metadata = md,
            UpdatedAtUtc = systemClock.UtcNow,
        };

        await sessionStore.SaveAsync(workspacePath, updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<ConversationSession> EnsureApprovalSettingsPersistedAsync(
        string workspacePath,
        ConversationSession session,
        ApprovalSettings? approvalSettings,
        CancellationToken cancellationToken)
    {
        if (approvalSettings is null)
        {
            return session;
        }

        var normalized = ApprovalSettingsResolver.Normalize(approvalSettings);
        var metadata = CloneMetadata(session.Metadata);
        var changed = false;

        if (normalized.AutoApproveScopes.Count == 0)
        {
            changed |= metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson);
        }
        else
        {
            var serializedScopes = JsonSerializer.Serialize(normalized.AutoApproveScopes.ToList(), ProtocolJsonContext.Default.ListApprovalScope);
            if (!metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson, out var currentScopes)
                || !string.Equals(currentScopes, serializedScopes, StringComparison.Ordinal))
            {
                metadata[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveScopesJson] = serializedScopes;
                changed = true;
            }
        }

        if (normalized.AutoApproveBudget is null)
        {
            changed |= metadata.Remove(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget);
        }
        else
        {
            var budgetText = normalized.AutoApproveBudget.Value.ToString(CultureInfo.InvariantCulture);
            if (!metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget, out var currentBudget)
                || !string.Equals(currentBudget, budgetText, StringComparison.Ordinal))
            {
                metadata[SharpClawWorkflowMetadataKeys.ApprovalAutoApproveBudget] = budgetText;
                changed = true;
            }
        }

        if (!changed)
        {
            return session;
        }

        var updated = session with
        {
            Metadata = metadata,
            UpdatedAtUtc = systemClock.UtcNow,
        };

        await sessionStore.SaveAsync(workspacePath, updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private static string FormatProviderFailureReason(ProviderExecutionException exception)
        => $"Provider failure ({exception.Kind}): {exception.Message}";

    private async Task<PrimaryMode?> ResolveSavedPrimaryModeAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
    {
        var session = await ResolveSessionAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
        return session?.Metadata is not null
            && session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.PrimaryMode, out var savedMode)
            && Enum.TryParse<PrimaryMode>(savedMode, true, out var persistedMode) && Enum.IsDefined(persistedMode)
                ? persistedMode
                : null;
    }

    private async Task<ConversationSession?> ResolveSessionAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            var resolvedSession = await sessionStore.GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
            if (resolvedSession is null)
            {
                throw new InvalidOperationException($"Session '{sessionId}' was not found in workspace '{workspacePath}'.");
            }

            return resolvedSession;
        }

        var attachedId = await workspaceSessionAttachmentStore
            .GetAttachedSessionIdAsync(workspacePath, cancellationToken)
            .ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(attachedId))
        {
            var attached = await sessionStore.GetByIdAsync(workspacePath, attachedId, cancellationToken).ConfigureAwait(false);
            if (attached is not null)
            {
                return attached;
            }
        }

        return await sessionStore.GetLatestAsync(workspacePath, cancellationToken).ConfigureAwait(false);
    }

    private RunPromptRequest EnrichRequestWithEditorIngress(string workspacePath, RunPromptRequest request)
    {
        var payload = editorContextBuffer.TryConsume(workspacePath);
        if (payload is null)
        {
            return request;
        }

        var md = request.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal);

        md["editor.workspaceRoot"] = pathService.GetFullPath(payload.WorkspaceRoot);
        if (!string.IsNullOrWhiteSpace(payload.CurrentFilePath))
        {
            md["editor.currentFile"] = payload.CurrentFilePath;
        }

        if (payload.Selection is not null)
        {
            md["editor.selection.text"] = payload.Selection.Text ?? string.Empty;
            md["editor.selection.start"] = payload.Selection.Start.ToString(CultureInfo.InvariantCulture);
            md["editor.selection.end"] = payload.Selection.End.ToString(CultureInfo.InvariantCulture);
        }

        var sessionId = request.SessionId;
        if (string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(payload.SessionId))
        {
            sessionId = payload.SessionId;
        }

        return request with
        {
            SessionId = sessionId,
            Metadata = md,
        };
    }

    private async Task AppendEventAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, List<RuntimeEvent> collectedEvents, CancellationToken cancellationToken)
    {
        await eventPublisher
            .PublishAsync(
                runtimeEvent,
                new RuntimeEventPublishOptions(
                    workspacePath,
                    sessionId,
                    PersistToSessionStore: true,
                    ThrowIfPersistenceFails: true),
                cancellationToken)
            .ConfigureAwait(false);
        collectedEvents.Add(runtimeEvent);
        await DispatchHookAsync(workspacePath, runtimeEvent, cancellationToken).ConfigureAwait(false);
    }

    private async Task AppendRuntimeEventsAsync(
        string workspacePath,
        string sessionId,
        IReadOnlyList<RuntimeEvent>? runtimeEventsToAppend,
        List<RuntimeEvent> collectedEvents,
        CancellationToken cancellationToken)
    {
        foreach (var runtimeEvent in runtimeEventsToAppend ?? [])
        {
            await AppendEventAsync(workspacePath, sessionId, runtimeEvent, collectedEvents, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ConversationSession> PreserveInterruptedMutationsAsync(string workspacePath, ConversationSession session, string turnId, IReadOnlyList<FileMutationOperation> operations, string? checkpointId)
    {
        if (operations.Count == 0) return session;
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var latest = await sessionStore.GetByIdAsync(workspacePath, session.Id, cleanup.Token).ConfigureAwait(false) ?? session;
        var id = checkpointId ?? CreateIdentifier("checkpoint");
        if (mutationSets is not null && await mutationSets.GetAsync(workspacePath, session.Id, id, cleanup.Token).ConfigureAwait(false) is not null) return latest;
        var checkpoint = new RuntimeCheckpoint(id, session.Id, turnId, systemClock.UtcNow, "Recoverable edits from an interrupted turn.",
            pathService.Combine(".sharpclaw", "sessions", session.Id, "checkpoints", id + ".json"), "Inspect or undo the recorded edits before continuing.", new Dictionary<string, string> { ["interrupted"] = "true" });
        await checkpointStore.SaveAsync(workspacePath, checkpoint, cleanup.Token).ConfigureAwait(false);
        latest = latest with { LastCheckpointId = id };
        return await checkpointMutationCoordinator.ApplyRecordedMutationsAsync(workspacePath, latest, turnId, id, operations, cleanup.Token).ConfigureAwait(false);
    }

    private async Task AppendProviderEventsAsync(
        string workspacePath,
        string sessionId,
        string turnId,
        TurnRunResult turnRunResult,
        List<RuntimeEvent> collectedEvents,
        CancellationToken cancellationToken)
    {
        if (turnRunResult.ProviderInvocations is { Count: > 0 } invocations)
        {
            foreach (var invocation in invocations)
            {
                await AppendProviderEventsAsync(workspacePath, sessionId, turnId,
                    turnRunResult with { ProviderRequest = invocation.Request, ProviderEvents = invocation.Events, ProviderInvocations = null },
                    collectedEvents, cancellationToken).ConfigureAwait(false);
            }
            return;
        }
        if (turnRunResult.ProviderRequest is null)
        {
            return;
        }

        var providerRequest = turnRunResult.ProviderRequest;
        await AppendEventAsync(
            workspacePath,
            sessionId,
            new ProviderStartedEvent(
                EventId: CreateIdentifier("event"),
                SessionId: sessionId,
                TurnId: turnId,
                OccurredAtUtc: systemClock.UtcNow,
                ProviderName: providerRequest.ProviderName,
                Model: providerRequest.Model,
                Request: providerRequest),
            collectedEvents,
            cancellationToken).ConfigureAwait(false);

        // Persist each run of streamed deltas in one append; a delta-per-write costs a lock, open, and flush per chunk.
        var pendingDeltas = new List<RuntimeEvent>();
        foreach (var providerEvent in turnRunResult.ProviderEvents ?? [])
        {
            if (!providerEvent.IsTerminal && !string.IsNullOrEmpty(providerEvent.Content))
            {
                pendingDeltas.Add(new ProviderDeltaEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: sessionId,
                    TurnId: turnId,
                    OccurredAtUtc: providerEvent.CreatedAtUtc,
                    ProviderName: providerRequest.ProviderName,
                    Model: providerRequest.Model,
                    ProviderEventId: providerEvent.Id,
                    Kind: providerEvent.Kind,
                    Content: providerEvent.Content));
            }

            if (providerEvent.IsTerminal)
            {
                await FlushDeltaEventsAsync(workspacePath, sessionId, pendingDeltas, collectedEvents, cancellationToken).ConfigureAwait(false);
                await AppendEventAsync(
                    workspacePath,
                    sessionId,
                    new ProviderCompletedEvent(
                        EventId: CreateIdentifier("event"),
                        SessionId: sessionId,
                        TurnId: turnId,
                        OccurredAtUtc: providerEvent.CreatedAtUtc,
                        ProviderName: providerRequest.ProviderName,
                        Model: providerRequest.Model,
                        ProviderEventId: providerEvent.Id,
                        Kind: providerEvent.Kind,
                        Usage: providerEvent.Usage),
                    collectedEvents,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        await FlushDeltaEventsAsync(workspacePath, sessionId, pendingDeltas, collectedEvents, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publishes buffered delta events as one batch. Deltas never trigger hooks, so none are dispatched.</summary>
    private async Task FlushDeltaEventsAsync(string workspacePath, string sessionId, List<RuntimeEvent> pendingDeltas, List<RuntimeEvent> collectedEvents, CancellationToken cancellationToken)
    {
        if (pendingDeltas.Count == 0)
        {
            return;
        }

        await eventPublisher
            .PublishRangeAsync(
                pendingDeltas.ToArray(),
                new RuntimeEventPublishOptions(
                    workspacePath,
                    sessionId,
                    PersistToSessionStore: true,
                    ThrowIfPersistenceFails: true),
                cancellationToken)
            .ConfigureAwait(false);
        collectedEvents.AddRange(pendingDeltas);
        pendingDeltas.Clear();
    }

    private async Task<ConversationSession> EnsureRecoverableFromFailedForPromptAsync(
        string workspacePath,
        ConversationSession session,
        List<RuntimeEvent> collectedEvents,
        CancellationToken cancellationToken)
    {
        if (session.State != SessionLifecycleState.Failed)
        {
            return session;
        }

        var previousState = session.State;
        var recoveringState = stateMachine.Transition(session.State, RuntimeLifecycleTransition.Recover);
        var now = systemClock.UtcNow;
        session = session with
        {
            State = recoveringState,
            UpdatedAtUtc = now,
        };

        await AppendEventAsync(
                workspacePath,
                session.Id,
                new SessionStateChangedEvent(
                    EventId: CreateIdentifier("event"),
                    SessionId: session.Id,
                    TurnId: null,
                    OccurredAtUtc: now,
                    PreviousState: previousState,
                    CurrentState: recoveringState,
                    Reason: "Session recovered from failure to begin a new prompt."),
                collectedEvents,
                cancellationToken)
            .ConfigureAwait(false);

        await sessionStore.SaveAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
        return session;
    }

    private async Task<ConversationSession> PersistTurnFailureAsync(
        string workspacePath,
        ConversationSession session,
        string turnId,
        List<RuntimeEvent> collectedEvents,
        string? reason,
        CancellationToken cancellationToken)
    {
        if (session.State == SessionLifecycleState.Archived)
        {
            logger.LogWarning(
                "Skipping Fail lifecycle transition for archived session {SessionId}. Reason: {Reason}",
                session.Id,
                reason);
            return session;
        }

        var failedState = stateMachine.Transition(session.State, RuntimeLifecycleTransition.Fail);
        var failedAtUtc = systemClock.UtcNow;
        var stateBeforeFailure = session.State;
        var updatedSession = session with
        {
            State = failedState,
            UpdatedAtUtc = failedAtUtc,
            ActiveTurnId = null,
        };

        await AppendEventAsync(
            workspacePath,
            updatedSession.Id,
            new SessionStateChangedEvent(
                EventId: CreateIdentifier("event"),
                SessionId: updatedSession.Id,
                TurnId: turnId,
                OccurredAtUtc: failedAtUtc,
                PreviousState: stateBeforeFailure,
                CurrentState: failedState,
                Reason: string.IsNullOrWhiteSpace(reason) ? FailedTurnReason : reason),
            collectedEvents,
            cancellationToken).ConfigureAwait(false);

        await sessionStore.SaveAsync(workspacePath, updatedSession, cancellationToken).ConfigureAwait(false);
        return updatedSession;
    }

    private async Task<RunPromptRequest> ApplyAgentAndConfigDefaultsAsync(
        string workspacePath,
        RunPromptRequest request,
        CancellationToken cancellationToken)
    {
        var config = await sharpClawConfigService.GetConfigAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var resolvedAgentId = string.IsNullOrWhiteSpace(request.AgentId)
            ? await ResolvePersistedAgentIdAsync(workspacePath, request.SessionId, cancellationToken).ConfigureAwait(false)
            : request.AgentId;
        var agent = await agentCatalogService
            .ResolveAsync(workspacePath, resolvedAgentId, resolvedAgentId, cancellationToken)
            .ConfigureAwait(false);
        var metadata = CloneMetadata(request.Metadata);

        resolvedAgentId = agent.Id;
        if (!string.IsNullOrWhiteSpace(agent.InstructionAppendix))
        {
            metadata[SharpClawWorkflowMetadataKeys.AgentInstructionAppendix] = agent.InstructionAppendix!;
        }

        if (!string.IsNullOrWhiteSpace(agent.Model)
            && (!metadata.TryGetValue("model", out var currentModel) || string.IsNullOrWhiteSpace(currentModel)))
        {
            metadata["model"] = agent.Model!;
        }

        if (agent.AllowedTools is { Length: > 0 })
        {
            metadata[SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = JsonSerializer.Serialize(
                agent.AllowedTools,
                ProtocolJsonContext.Default.StringArray);
        }

        metadata[SharpClawWorkflowMetadataKeys.ActiveAgentId] = agent.Id;

        if (request.PrimaryMode is null && agent.PrimaryMode is not null)
        {
            request = request with { PrimaryMode = agent.PrimaryMode };
        }

        if ((config.Document.ShareMode ?? ShareMode.Manual) != ShareMode.Manual)
        {
            metadata["shareMode"] = config.Document.ShareMode!.Value.ToString();
        }

        return request with
        {
            AgentId = resolvedAgentId,
            Metadata = metadata,
        };
    }

    private async Task<string?> ResolvePersistedAgentIdAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var session = await sessionStore.GetByIdAsync(workspacePath, sessionId, cancellationToken).ConfigureAwait(false);
        if (session?.Metadata is null
            || !session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.ActiveAgentId, out var agentId)
            || string.IsNullOrWhiteSpace(agentId))
        {
            return null;
        }

        return agentId;
    }

    private async Task<bool> ShouldAutoShareAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var config = await sharpClawConfigService.GetConfigAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return (config.Document.ShareMode ?? ShareMode.Manual) == ShareMode.Auto;
    }

    private Task DispatchHookAsync(string workspacePath, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
    {
        var trigger = runtimeEvent switch
        {
            TurnStartedEvent => HookTriggerKind.TurnStarted,
            TurnCompletedEvent => HookTriggerKind.TurnCompleted,
            ToolStartedEvent => HookTriggerKind.ToolStarted,
            ToolCompletedEvent => HookTriggerKind.ToolCompleted,
            ShareCreatedEvent => HookTriggerKind.ShareCreated,
            ShareRemovedEvent => HookTriggerKind.ShareRemoved,
            _ => (HookTriggerKind?)null
        };

        if (trigger is null)
        {
            return Task.CompletedTask;
        }

        var payload = JsonSerializer.Serialize(runtimeEvent, runtimeEvent.GetType(), ProtocolJsonContext.Default);
        return hookDispatcher.DispatchAsync(workspacePath, trigger.Value, payload, cancellationToken);
    }

    private string NormalizeWorkspacePath(string? workspacePath)
        => pathService.GetFullPath(string.IsNullOrWhiteSpace(workspacePath) ? pathService.GetCurrentDirectory() : workspacePath);

    /// <summary>
    /// Workspace markdown cannot raise permission above the caller's mode; lower modes win.
    /// </summary>
    private static PermissionMode ClampPermissionModeForCustomCommand(PermissionMode context, PermissionMode? commandOverride)
    {
        var effective = commandOverride ?? context;
        return (PermissionMode)Math.Min((int)context, (int)effective);
    }

    /// <summary>
    /// Workspace markdown cannot disable plan mode: if either side is <see cref="PrimaryMode.Plan"/>, plan wins.
    /// </summary>
    private static PrimaryMode ClampPrimaryModeForCustomCommand(PrimaryMode context, PrimaryMode? commandOverride)
    {
        var effective = commandOverride ?? context;
        if (context == PrimaryMode.Plan || effective == PrimaryMode.Plan)
        {
            return PrimaryMode.Plan;
        }

        if (context == PrimaryMode.Research || effective == PrimaryMode.Research)
        {
            return PrimaryMode.Research;
        }

        if (context == PrimaryMode.Spec || effective == PrimaryMode.Spec)
        {
            return PrimaryMode.Spec;
        }

        return PrimaryMode.Build;
    }

    private static string FormatSpecCompletionMessage(SpecArtifactSet artifacts)
        => string.Join(
            Environment.NewLine,
            [
                $"Spec generated: {artifacts.RootPath}",
                $"Requirements: {artifacts.RequirementsPath}",
                $"Design: {artifacts.DesignPath}",
                $"Tasks: {artifacts.TasksPath}"
            ]);
}
