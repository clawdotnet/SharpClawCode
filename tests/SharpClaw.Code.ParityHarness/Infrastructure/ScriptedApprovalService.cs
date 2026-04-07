using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.ParityHarness.Infrastructure;

/// <summary>
/// Deterministic approval behavior for non-interactive parity runs.
/// </summary>
internal sealed class ScriptedApprovalService(bool approve) : IApprovalService
{
    private static readonly DateTimeOffset ResolvedAtUtc = new(2026, 4, 6, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        return Task.FromResult(new ApprovalDecision(
            request.Scope,
            approve,
            request.RequestedBy,
            "scripted-approval",
            approve ? "Approved by scripted harness." : "Denied by scripted harness.",
            ResolvedAtUtc,
            null));
    }
}
