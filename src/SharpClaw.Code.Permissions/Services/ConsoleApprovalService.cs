using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Prompts for interactive console approval.
/// </summary>
public sealed class ConsoleApprovalService : IApprovalService
{
    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        Console.WriteLine($"Approval required for {request.ToolName} ({request.Scope}).");
        Console.WriteLine(request.Prompt);
        if (request.CanRememberDecision)
        {
            Console.WriteLine("This approval may be remembered for the rest of the session.");
        }

        Console.Write("Allow? [y/N]: ");
        var response = Console.ReadLine();
        var approved = string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

        return Task.FromResult(new ApprovalDecision(
            Scope: request.Scope,
            Approved: approved,
            RequestedBy: request.RequestedBy,
            ResolvedBy: "console",
            Reason: approved ? "Approved via interactive console." : "Denied via interactive console.",
            ResolvedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null,
            RememberForSession: approved && request.CanRememberDecision));
    }
}
