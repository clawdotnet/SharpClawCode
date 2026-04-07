using System.Text.Json;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Evaluates tool execution requests through ordered permission rules and approval flow.
/// </summary>
public sealed class PermissionPolicyEngine(
    IEnumerable<IPermissionRule> rules,
    IApprovalService approvalService,
    ISessionApprovalMemory sessionApprovalMemory) : IPermissionPolicyEngine
{
    private readonly IPermissionRule[] orderedRules = rules.ToArray();

    /// <inheritdoc />
    public async Task<PermissionDecision> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var rule in orderedRules)
        {
            var result = await rule.EvaluateAsync(request, context, cancellationToken).ConfigureAwait(false);
            switch (result.Outcome)
            {
                case PermissionRuleOutcome.Abstain:
                    continue;
                case PermissionRuleOutcome.Allow:
                    return CreateDecision(request.ApprovalScope, context.PermissionMode, true, result.Reason);
                case PermissionRuleOutcome.Deny:
                    return CreateDecision(request.ApprovalScope, context.PermissionMode, false, result.Reason);
                case PermissionRuleOutcome.RequireApproval:
                    return await ResolveApprovalAsync(request, context, result, cancellationToken).ConfigureAwait(false);
                default:
                    throw new InvalidOperationException($"Unhandled permission rule outcome '{result.Outcome}'.");
            }
        }

        return await EvaluateByModeAsync(request, context, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PermissionDecision> EvaluateByModeAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        return context.PermissionMode switch
        {
            PermissionMode.ReadOnly => await EvaluateReadOnlyModeAsync(request, context, cancellationToken).ConfigureAwait(false),
            PermissionMode.WorkspaceWrite => request.ApprovalScope switch
            {
                ApprovalScope.ToolExecution or ApprovalScope.FileSystemWrite => CreateDecision(
                    request.ApprovalScope,
                    context.PermissionMode,
                    true,
                    "Workspace-write mode allows workspace reads and writes."),
                ApprovalScope.PromptOutsideWorkspaceRead => await ResolveApprovalAsync(
                    request,
                    context,
                    PermissionRuleResult.RequireApproval(
                        "Reading a file outside the workspace for prompt context requires approval.",
                        canRememberApproval: true),
                    cancellationToken).ConfigureAwait(false),
                ApprovalScope.ShellExecution or ApprovalScope.NetworkAccess or ApprovalScope.SessionOperation
                    => await ResolveApprovalAsync(
                        request,
                        context,
                        PermissionRuleResult.RequireApproval("Workspace-write mode requires approval for elevated operations."),
                        cancellationToken).ConfigureAwait(false),
                _ => CreateDecision(request.ApprovalScope, context.PermissionMode, false, "The requested approval scope is not supported.")
            },
            PermissionMode.DangerFullAccess => CreateDecision(
                request.ApprovalScope,
                context.PermissionMode,
                true,
                "Danger-full-access mode allows the request."),
            _ => CreateDecision(request.ApprovalScope, context.PermissionMode, false, "The permission mode is not supported.")
        };
    }

    private async Task<PermissionDecision> EvaluateReadOnlyModeAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (request.ApprovalScope == ApprovalScope.PromptOutsideWorkspaceRead)
        {
            return await ResolveApprovalAsync(
                request,
                context,
                PermissionRuleResult.RequireApproval(
                    "Reading a file outside the workspace for prompt context requires approval.",
                    canRememberApproval: true),
                cancellationToken).ConfigureAwait(false);
        }

        var allowed = !request.IsDestructive && request.ApprovalScope is ApprovalScope.ToolExecution;
        return CreateDecision(
            request.ApprovalScope,
            context.PermissionMode,
            allowed,
            allowed
                ? "Read-only mode allows non-destructive tool execution."
                : $"Permission mode '{context.PermissionMode}' blocks destructive or elevated tools.");
    }

    private async Task<PermissionDecision> ResolveApprovalAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        PermissionRuleResult ruleResult,
        CancellationToken cancellationToken)
    {
        var approvalKey = CreateApprovalKey(request, context);
        if (ruleResult.CanRememberApproval && sessionApprovalMemory.TryGet(context.SessionId, approvalKey) is { } rememberedEntry && rememberedEntry.Decision.Approved)
        {
            return CreateDecision(
                request.ApprovalScope,
                context.PermissionMode,
                true,
                $"Reused remembered approval for '{request.ToolName}'.");
        }

        var approvalRequest = new ApprovalRequest(
            Scope: request.ApprovalScope,
            ToolName: request.ToolName,
            Prompt: ruleResult.Reason ?? $"Approval is required for '{request.ToolName}'.",
            RequestedBy: "permission-policy-engine",
            CanRememberDecision: ruleResult.CanRememberApproval);

        var approvalDecision = await approvalService.RequestApprovalAsync(approvalRequest, context, cancellationToken).ConfigureAwait(false);
        if (approvalDecision.Approved && ruleResult.CanRememberApproval)
        {
            sessionApprovalMemory.Store(context.SessionId, approvalKey, new ApprovalMemoryEntry(approvalDecision));
        }

        return CreateDecision(
            request.ApprovalScope,
            context.PermissionMode,
            approvalDecision.Approved,
            CombineReasons(ruleResult.Reason, approvalDecision.Reason));
    }

    private static PermissionDecision CreateDecision(ApprovalScope scope, PermissionMode mode, bool isAllowed, string? reason)
        => new(scope, mode, isAllowed, reason, DateTimeOffset.UtcNow);

    private static string? CombineReasons(string? primaryReason, string? secondaryReason)
    {
        if (string.IsNullOrWhiteSpace(primaryReason))
        {
            return secondaryReason;
        }

        if (string.IsNullOrWhiteSpace(secondaryReason) || string.Equals(primaryReason, secondaryReason, StringComparison.Ordinal))
        {
            return primaryReason;
        }

        return $"{primaryReason} {secondaryReason}";
    }

    // Session-local key for remembered approvals; plugin id and manifest trust avoid cross-plugin/tier reuse.
    private static string CreateApprovalKey(ToolExecutionRequest request, PermissionEvaluationContext context)
    {
        var trustSegment = context.ToolOriginatingPluginTrust is { } trust
            ? trust.ToString()
            : string.Empty;
        var pathSegment = request.ApprovalScope == ApprovalScope.PromptOutsideWorkspaceRead
            ? TryReadPathArgument(request.ArgumentsJson)
            : string.Empty;
        return string.Join(
            "::",
            request.ToolName,
            request.ApprovalScope,
            context.SourceKind,
            context.SourceName ?? string.Empty,
            request.WorkingDirectory ?? string.Empty,
            context.ToolOriginatingPluginId ?? string.Empty,
            trustSegment,
            pathSegment);
    }

    private static string TryReadPathArgument(string argumentsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            return document.RootElement.TryGetProperty("path", out var pathElement)
                   && pathElement.ValueKind == JsonValueKind.String
                ? pathElement.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
