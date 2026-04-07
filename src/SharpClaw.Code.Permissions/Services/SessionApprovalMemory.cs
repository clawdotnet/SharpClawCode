using System.Collections.Concurrent;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Stores remembered approvals in memory for the lifetime of the process.
/// </summary>
public sealed class SessionApprovalMemory : ISessionApprovalMemory
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ApprovalMemoryEntry>> approvals = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public ApprovalMemoryEntry? TryGet(string sessionId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (!approvals.TryGetValue(sessionId, out var sessionApprovals))
        {
            return null;
        }

        return sessionApprovals.TryGetValue(key, out var entry)
            ? entry
            : null;
    }

    /// <inheritdoc />
    public void Store(string sessionId, string key, ApprovalMemoryEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(entry);

        var sessionApprovals = approvals.GetOrAdd(sessionId, static _ => new ConcurrentDictionary<string, ApprovalMemoryEntry>(StringComparer.Ordinal));
        sessionApprovals[key] = entry;
    }
}
