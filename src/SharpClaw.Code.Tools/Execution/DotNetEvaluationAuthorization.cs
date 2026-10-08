using System.Text.Json;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Execution;

/// <summary>Requires a separate execution decision before MSBuild evaluation, with the original caller's policy and correlation.</summary>
internal sealed class DotNetEvaluationAuthorization(IPermissionPolicyEngine policy, ToolExecutionContext context, ToolExecutionRequest original, IRuntimeEventPublisher? publisher, IRuntimeHostContextAccessor? host) : IDotNetWorkspaceEvaluationAuthorization
{
    public async Task<bool> AuthorizeAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var evaluation = original with { Id = original.Id + "-evaluation", ArgumentsJson = JsonSerializer.Serialize(request, ProtocolJsonContext.Default.DotNetWorkspaceRequest), ApprovalScope = ApprovalScope.ShellExecution, RequiresApproval = true, IsDestructive = false, WorkingDirectory = request.WorkspaceRoot };
        var policyContext = new PermissionEvaluationContext(context.SessionId, context.WorkspaceRoot, request.WorkspaceRoot, context.PermissionMode, context.AllowedTools, context.AllowDangerousBypass, context.IsInteractive, context.SourceKind, context.SourceName, context.TrustedPluginNames, context.TrustedMcpServerNames, PrimaryMode: context.PrimaryMode, TenantId: host?.Current?.TenantId, ApprovalSettings: context.ApprovalSettings);
        var options = new RuntimeEventPublishOptions(context.WorkspaceRoot, context.SessionId, PersistToSessionStore: !string.IsNullOrWhiteSpace(context.SessionId), ThrowIfPersistenceFails: !string.IsNullOrWhiteSpace(context.SessionId));
        if (publisher is not null) await publisher.PublishAsync(new PermissionRequestedEvent("event-" + Guid.NewGuid().ToString("N"), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, evaluation), options, cancellationToken).ConfigureAwait(false);
        var decision = await policy.EvaluateAsync(evaluation, policyContext, cancellationToken).ConfigureAwait(false);
        if (publisher is not null) await publisher.PublishAsync(new PermissionResolvedEvent("event-" + Guid.NewGuid().ToString("N"), context.SessionId, context.TurnId, DateTimeOffset.UtcNow, decision), options, cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed;
    }
}
