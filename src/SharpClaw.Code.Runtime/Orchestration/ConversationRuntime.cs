using System.Collections.Concurrent;
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

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Implements durable conversation runtime orchestration for prompt execution.
/// </summary>
public sealed class ConversationRuntime(
    ISessionStore sessionStore,
    IEventStore eventStore,
    IRuntimeEventPublisher eventPublisher,
    ICheckpointStore checkpointStore,
    ITurnRunner turnRunner,
    IRuntimeStateMachine stateMachine,
    ISystemClock systemClock,
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    IRuntimeHostContextAccessor hostContextAccessor,
    IOperationalDiagnosticsCoordinator operationalDiagnostics,
    ICustomCommandDiscoveryService customCommandDiscovery,
    ISessionExportService sessionExportService,
    IWorkspaceSessionAttachmentStore workspaceSessionAttachmentStore,
    IEditorContextBuffer editorContextBuffer,
    CheckpointMutationCoordinator checkpointMutationCoordinator,
    ISessionCoordinator sessionCoordinator,
    IPortableSessionBundleService portableSessionBundleService,
    ISpecWorkflowService specWorkflowService,
    ISharpClawConfigService sharpClawConfigService,
    IAgentCatalogService agentCatalogService,
    IShareSessionService shareSessionService,
    IConversationCompactionService conversationCompactionService,
    IHookDispatcher hookDispatcher,
    ILogger<ConversationRuntime> logger) : IConversationRuntime, IRuntimeCommandService
{
    private const string LastTurnSequenceKey = "lastTurnSequence";
    private const string CanceledTurnReason = "The turn was canceled.";
    private const string FailedTurnReason = "The turn failed.";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SessionMutexes = new(StringComparer.Ordinal);

    private static SemaphoreSlim GetSessionMutex(string workspacePath, string sessionId)
        => SessionMutexes.GetOrAdd($"{workspacePath}\u0000{sessionId}", static _ => new SemaphoreSlim(1, 1));

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
        request = await ApplyAgentAndConfigDefaultsAsync(workspacePath, request, cancellationToken).ConfigureAwait(false);
        var runtimeEvents = new List<RuntimeEvent>();
        var session = await ResolveSessionAsync(workspacePath, request, cancellationToken).ConfigureAwait(false);
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

        session = await EnsurePrimaryModePersistedAsync(workspacePath, session, request.PrimaryMode, cancellationToken).ConfigureAwait(false);

        var sessionMutex = GetSessionMutex(workspacePath, session.Id);
        await sessionMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var turnLockPath = storagePathResolver.GetSessionTurnLockPath(workspacePath, session.Id);
            await using var crossProcessTurnLock = await fileSystem
                .AcquireExclusiveFileLockAsync(turnLockPath, cancellationToken)
                .ConfigureAwait(false);

            // Re-read the session inside the lock so the turn sequence read-modify-write is consistent across concurrent prompts.
            var refreshed = await sessionStore.GetByIdAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);
            if (refreshed is not null)
            {
                session = refreshed;
            }

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

        try
        {
            var effectivePrimary = PrimaryModeResolver.ResolveEffective(request, session);
            var runnerRequest = request with
            {
                WorkingDirectory = workspacePath,
                Metadata = MergeMetadata(request.Metadata, request.PermissionMode, request.OutputFormat, effectivePrimary)
            };

            var turnRunResult = await turnRunner.RunAsync(session, turn, runnerRequest, cancellationToken).ConfigureAwait(false);
            SpecArtifactSet? specArtifacts = null;
            if (effectivePrimary == PrimaryMode.Spec)
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
            };
            ConversationHistoryCache.StoreCompletedTurn(workspacePath, session.Id, completedTurn);

            await AppendRuntimeEventsAsync(
                workspacePath,
                session.Id,
                turnRunResult.RuntimeEvents,
                runtimeEvents,
                cancellationToken).ConfigureAwait(false);

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
                State = activeState,
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
                    Succeeded: true,
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
                SpecArtifacts: finalSpecArtifacts);
        }
        catch (OperationCanceledException exception)
        {
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
        }
        finally
        {
            sessionMutex.Release();
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
                HostContext: context.HostContext),
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
        var contextPrimary = context.PrimaryMode ?? PrimaryMode.Build;
        var primary = ClampPrimaryModeForCustomCommand(contextPrimary, definition.PrimaryModeOverride);

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
                HostContext: context.HostContext),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode);
        var report = await operationalDiagnostics
            .BuildStatusReportAsync(input, cancellationToken)
            .ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(report, ProtocolJsonContext.Default.RuntimeStatusReport);
        var message = FormatStatusMessage(report);

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode);
        var report = await operationalDiagnostics.RunDoctorAsync(input, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(report, ProtocolJsonContext.Default.DoctorReport);
        var exitCode = report.OverallStatus == OperationalCheckStatus.Error ? 1 : 0;
        var message = FormatDoctorMessage(report);

        return new CommandResult(
            Succeeded: exitCode == 0,
            ExitCode: exitCode,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <inheritdoc />
    public async Task<CommandResult> InspectSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode);
        var inspection = await operationalDiagnostics
            .InspectSessionAsync(sessionId, input, cancellationToken)
            .ConfigureAwait(false);
        if (inspection is null)
        {
            return new CommandResult(
                Succeeded: false,
                ExitCode: 1,
                OutputFormat: context.OutputFormat,
                Message: "No matching session found.",
                DataJson: null);
        }

        var payload = JsonSerializer.Serialize(inspection, ProtocolJsonContext.Default.SessionInspectionReport);
        var message =
            $"{inspection.Session.Id} ({inspection.Session.State}) · {inspection.PersistedEventCount} persisted events";

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    private static string FormatDoctorMessage(DoctorReport report)
        => string.Join(
            Environment.NewLine,
            report.Checks.Select(static c =>
            {
                var detail = string.IsNullOrWhiteSpace(c.Detail) ? string.Empty : $" — {c.Detail}";
                return $"[{c.Status}] {c.Id}: {c.Summary}{detail}";
            }));

    private static string FormatStatusMessage(RuntimeStatusReport report)
    {
        var sessionLabel = report.LatestSessionId is null
            ? "no session"
            : $"{report.LatestSessionId} ({report.LatestSessionState})";
        var headline =
            $"Ready · {report.WorkspaceRoot} · model {report.SelectedModel} · mode {report.PrimaryMode} · latest {sessionLabel} · "
            + $"MCP {report.McpReadyCount}/{report.McpServerCount} · plugins {report.PluginEnabledCount}/{report.PluginInstalledCount} · "
            + $"LSP {report.LspServerCount} · diagnostics {report.DiagnosticsErrorCount} error(s), {report.DiagnosticsWarningCount} warning(s)";
        var notable = report.Checks.FirstOrDefault(static c
            => c.Status is not OperationalCheckStatus.Ok and not OperationalCheckStatus.Skipped);
        return notable is null ? headline : $"{headline}{Environment.NewLine}[{notable.Status}] {notable.Id}: {notable.Summary}";
    }

    private static string FormatProviderFailureReason(ProviderExecutionException exception)
        => $"Provider failure ({exception.Kind}): {exception.Message}";

    private async Task<ConversationSession?> ResolveSessionAsync(string workspacePath, RunPromptRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var resolvedSession = await sessionStore.GetByIdAsync(workspacePath, request.SessionId, cancellationToken).ConfigureAwait(false);
            if (resolvedSession is null)
            {
                throw new InvalidOperationException($"Session '{request.SessionId}' was not found in workspace '{workspacePath}'.");
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

    private async Task AppendProviderEventsAsync(
        string workspacePath,
        string sessionId,
        string turnId,
        TurnRunResult turnRunResult,
        List<RuntimeEvent> collectedEvents,
        CancellationToken cancellationToken)
    {
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

        foreach (var providerEvent in turnRunResult.ProviderEvents ?? [])
        {
            if (!providerEvent.IsTerminal && !string.IsNullOrWhiteSpace(providerEvent.Content))
            {
                await AppendEventAsync(
                    workspacePath,
                    sessionId,
                    new ProviderDeltaEvent(
                        EventId: CreateIdentifier("event"),
                        SessionId: sessionId,
                        TurnId: turnId,
                        OccurredAtUtc: providerEvent.CreatedAtUtc,
                        ProviderName: providerRequest.ProviderName,
                        Model: providerRequest.Model,
                        ProviderEventId: providerEvent.Id,
                        Kind: providerEvent.Kind,
                        Content: providerEvent.Content),
                    collectedEvents,
                    cancellationToken).ConfigureAwait(false);
            }

            if (providerEvent.IsTerminal)
            {
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

    private async Task<T> ExecuteWithSessionLockAsync<T>(
        string workspacePath,
        string sessionId,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(operation);

        var sessionMutex = GetSessionMutex(workspacePath, sessionId);
        await sessionMutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var turnLockPath = storagePathResolver.GetSessionTurnLockPath(workspacePath, sessionId);
            await using var crossProcessTurnLock = await fileSystem
                .AcquireExclusiveFileLockAsync(turnLockPath, cancellationToken)
                .ConfigureAwait(false);
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sessionMutex.Release();
        }
    }

    private static string CreateIdentifier(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";

    private static int GetLastTurnSequence(ConversationSession session)
        => session.Metadata is not null
           && session.Metadata.TryGetValue(LastTurnSequenceKey, out var sequenceText)
           && int.TryParse(sequenceText, CultureInfo.InvariantCulture, out var sequence)
            ? sequence
            : 0;

    private static Dictionary<string, string> CloneMetadata(Dictionary<string, string>? metadata)
        => metadata is null
            ? []
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);

    private static Dictionary<string, string> MergeMetadata(
        Dictionary<string, string>? metadata,
        PermissionMode permissionMode,
        OutputFormat outputFormat,
        PrimaryMode primaryMode)
    {
        var merged = CloneMetadata(metadata);
        merged["permissionMode"] = permissionMode.ToString();
        merged["outputFormat"] = outputFormat.ToString();
        merged[SharpClawWorkflowMetadataKeys.PrimaryMode] = primaryMode.ToString();
        return merged;
    }

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
