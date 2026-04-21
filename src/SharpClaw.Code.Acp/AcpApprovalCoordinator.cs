using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Acp;

/// <summary>
/// Coordinates ACP approval notifications and replies for a single host process.
/// </summary>
public sealed class AcpApprovalCoordinator
{
    private readonly ConcurrentDictionary<string, PendingApproval> pending = new(StringComparer.Ordinal);
    private Func<JsonObject, Task>? notificationSink;

    /// <summary>
    /// Gets a value indicating whether the connected ACP client advertised approval round-trips.
    /// </summary>
    public bool SupportsApprovals { get; private set; }

    /// <summary>
    /// Updates the active ACP notification sink and approval capability state for the current host run.
    /// </summary>
    /// <param name="supportsApprovals">Whether the client supports approval callbacks.</param>
    /// <param name="notificationWriter">Notification writer used for approval requests.</param>
    public void Configure(bool supportsApprovals, Func<JsonObject, Task> notificationWriter)
    {
        SupportsApprovals = supportsApprovals;
        notificationSink = notificationWriter;
    }

    /// <summary>
    /// Sends an approval request to the connected ACP client and waits for the response.
    /// </summary>
    /// <param name="request">Approval request details.</param>
    /// <param name="context">Permission evaluation context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved approval decision.</returns>
    public async Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (!SupportsApprovals || notificationSink is null)
        {
            return new ApprovalDecision(
                request.Scope,
                Approved: false,
                RequestedBy: request.RequestedBy,
                ResolvedBy: "acp",
                Reason: "ACP client does not support approval round-trips.",
                ResolvedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                RememberForSession: false);
        }

        var requestId = $"approval-{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<(bool Approved, bool Remember)>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[requestId] = new PendingApproval(context.SessionId, request, tcs);

        using var registration = cancellationToken.Register(() =>
        {
            if (pending.TryRemove(requestId, out var removed))
            {
                removed.Completion.TrySetCanceled(cancellationToken);
            }
        });

        await notificationSink(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "session/notification",
            ["params"] = new JsonObject
            {
                ["sessionId"] = context.SessionId,
                ["update"] = new JsonObject
                {
                    ["sessionUpdate"] = "approvalRequest",
                    ["approval"] = new JsonObject
                    {
                        ["requestId"] = requestId,
                        ["scope"] = request.Scope.ToString(),
                        ["toolName"] = request.ToolName,
                        ["prompt"] = request.Prompt,
                        ["canRememberDecision"] = request.CanRememberDecision,
                    },
                },
            },
        }).ConfigureAwait(false);

        var response = await tcs.Task.ConfigureAwait(false);
        return new ApprovalDecision(
            request.Scope,
            Approved: response.Approved,
            RequestedBy: request.RequestedBy,
            ResolvedBy: "acp",
            Reason: response.Approved ? "Approved via ACP client." : "Denied via ACP client.",
            ResolvedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null,
            RememberForSession: response.Remember);
    }

    /// <summary>
    /// Resolves a pending ACP approval request.
    /// </summary>
    /// <param name="requestId">Approval request id.</param>
    /// <param name="approved">Whether the request was approved.</param>
    /// <param name="remember">Whether the decision should be remembered for the session.</param>
    /// <returns><see langword="true"/> when a pending request was resolved; otherwise <see langword="false"/>.</returns>
    public bool TryResolve(string requestId, bool approved, bool remember)
    {
        if (!pending.TryRemove(requestId, out var pendingApproval))
        {
            return false;
        }

        pendingApproval.Completion.TrySetResult((approved, remember));
        return true;
    }

    private sealed record PendingApproval(
        string SessionId,
        ApprovalRequest Request,
        TaskCompletionSource<(bool Approved, bool Remember)> Completion);
}
