using System.Collections.Concurrent;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Maintains process-local auto-approval budget counters keyed by tenant-aware session id.
/// </summary>
public sealed class AutoApprovalBudgetTracker : IAutoApprovalBudgetTracker
{
    private readonly ConcurrentDictionary<string, int> usage = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    /// <inheritdoc />
    public bool TryConsume(PermissionEvaluationContext context, ApprovalScope scope, int budget, out int remainingBudget)
    {
        ArgumentNullException.ThrowIfNull(context);

        remainingBudget = 0;
        if (budget <= 0)
        {
            return false;
        }

        var key = string.IsNullOrWhiteSpace(context.TenantId)
            ? context.SessionId
            : $"{context.TenantId}::{context.SessionId}";

        lock (gate)
        {
            usage.TryGetValue(key, out var consumed);
            if (consumed >= budget)
            {
                return false;
            }

            consumed++;
            usage[key] = consumed;
            remainingBudget = budget - consumed;
            return true;
        }
    }
}
