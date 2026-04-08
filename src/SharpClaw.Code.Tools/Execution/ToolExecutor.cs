using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Execution;

/// <summary>
/// Mediates registry lookup, permission evaluation, and tool execution.
/// </summary>
public sealed class ToolExecutor(
    IToolRegistry toolRegistry,
    IPermissionPolicyEngine permissionPolicyEngine,
    IRuntimeEventPublisher? eventPublisher = null,
    ILogger<ToolExecutor>? logger = null) : IToolExecutor
{
    private readonly ILogger<ToolExecutor> logger = logger ?? NullLogger<ToolExecutor>.Instance;

    /// <inheritdoc />
    public async Task<ToolExecutionEnvelope> ExecuteAsync(
        string toolName,
        string argumentsJson,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentException.ThrowIfNullOrWhiteSpace(argumentsJson);
        ArgumentNullException.ThrowIfNull(context);

        var tool = await toolRegistry
            .GetRequiredAsync(toolName, context.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var request = new ToolExecutionRequest(
            Id: $"tool-request-{Guid.NewGuid():N}",
            SessionId: context.SessionId,
            TurnId: context.TurnId,
            ToolName: tool.Definition.Name,
            ArgumentsJson: argumentsJson,
            ApprovalScope: tool.Definition.ApprovalScope,
            WorkingDirectory: context.WorkingDirectory,
            RequiresApproval: tool.Definition.RequiresApproval,
            IsDestructive: tool.Definition.IsDestructive);

        var pluginSource = tool.PluginSource;
        var evaluationContext = new PermissionEvaluationContext(
            SessionId: context.SessionId,
            WorkspaceRoot: context.WorkspaceRoot,
            WorkingDirectory: context.WorkingDirectory,
            PermissionMode: context.PermissionMode,
            AllowedTools: context.AllowedTools,
            AllowDangerousBypass: context.AllowDangerousBypass,
            IsInteractive: context.IsInteractive,
            SourceKind: context.SourceKind,
            SourceName: context.SourceName,
            TrustedPluginNames: context.TrustedPluginNames,
            TrustedMcpServerNames: context.TrustedMcpServerNames,
            ToolOriginatingPluginId: pluginSource?.PluginId,
            ToolOriginatingPluginTrust: pluginSource?.Trust,
            PrimaryMode: context.PrimaryMode);

        var publishOptions = CreatePublishOptions(context);
        var now = DateTimeOffset.UtcNow;

        await PublishAsync(
            new ToolStartedEvent(
                EventId: CreateEventId(),
                SessionId: context.SessionId,
                TurnId: context.TurnId,
                OccurredAtUtc: now,
                Request: request),
            publishOptions,
            cancellationToken).ConfigureAwait(false);

        await PublishAsync(
            new PermissionRequestedEvent(
                EventId: CreateEventId(),
                SessionId: context.SessionId,
                TurnId: context.TurnId,
                OccurredAtUtc: now,
                Request: request),
            publishOptions,
            cancellationToken).ConfigureAwait(false);

        var permissionDecision = await permissionPolicyEngine
            .EvaluateAsync(request, evaluationContext, cancellationToken)
            .ConfigureAwait(false);

        await PublishAsync(
            new PermissionResolvedEvent(
                EventId: CreateEventId(),
                SessionId: context.SessionId,
                TurnId: context.TurnId,
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Decision: permissionDecision),
            publishOptions,
            cancellationToken).ConfigureAwait(false);

        if (!permissionDecision.IsAllowed)
        {
            var deniedResult = new ToolResult(
                RequestId: request.Id,
                ToolName: request.ToolName,
                Succeeded: false,
                OutputFormat: context.OutputFormat,
                Output: null,
                ErrorMessage: permissionDecision.Reason,
                ExitCode: 1,
                DurationMilliseconds: 0,
                StructuredOutputJson: null);

            await PublishAsync(
                new ToolCompletedEvent(
                    EventId: CreateEventId(),
                    SessionId: context.SessionId,
                    TurnId: context.TurnId,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Result: deniedResult),
                publishOptions,
                cancellationToken).ConfigureAwait(false);

            return new ToolExecutionEnvelope(request, permissionDecision, deniedResult);
        }

        try
        {
            var result = await tool.ExecuteAsync(context, request, cancellationToken).ConfigureAwait(false);
            await PublishAsync(
                new ToolCompletedEvent(
                    EventId: CreateEventId(),
                    SessionId: context.SessionId,
                    TurnId: context.TurnId,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Result: result),
                publishOptions,
                cancellationToken).ConfigureAwait(false);
            return new ToolExecutionEnvelope(request, permissionDecision, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            this.logger.LogWarning(
                exception,
                "Tool '{ToolName}' threw {ExceptionType} during execution for session {SessionId}, turn {TurnId}.",
                request.ToolName,
                exception.GetType().Name,
                context.SessionId,
                context.TurnId);
            var failedResult = new ToolResult(
                RequestId: request.Id,
                ToolName: request.ToolName,
                Succeeded: false,
                OutputFormat: context.OutputFormat,
                Output: null,
                ErrorMessage: exception.Message,
                ExitCode: 1,
                DurationMilliseconds: 0,
                StructuredOutputJson: null);

            await PublishAsync(
                new ToolCompletedEvent(
                    EventId: CreateEventId(),
                    SessionId: context.SessionId,
                    TurnId: context.TurnId,
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    Result: failedResult),
                publishOptions,
                cancellationToken).ConfigureAwait(false);

            return new ToolExecutionEnvelope(request, permissionDecision, failedResult);
        }
    }

    private static RuntimeEventPublishOptions CreatePublishOptions(ToolExecutionContext context)
        => new(
            context.WorkspaceRoot,
            context.SessionId,
            PersistToSessionStore: !string.IsNullOrWhiteSpace(context.SessionId),
            ThrowIfPersistenceFails: !string.IsNullOrWhiteSpace(context.SessionId));

    private async ValueTask PublishAsync(
        RuntimeEvent runtimeEvent,
        RuntimeEventPublishOptions options,
        CancellationToken cancellationToken)
    {
        if (eventPublisher is null)
        {
            return;
        }

        await eventPublisher.PublishAsync(runtimeEvent, options, cancellationToken).ConfigureAwait(false);
    }

    private static string CreateEventId() => $"event-{Guid.NewGuid():N}";
}
