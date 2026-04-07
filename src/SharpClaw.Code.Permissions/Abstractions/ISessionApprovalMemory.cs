using SharpClaw.Code.Permissions.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Stores session-scoped approval decisions for repeated requests.
/// </summary>
public interface ISessionApprovalMemory
{
    /// <summary>
    /// Attempts to retrieve a remembered approval for the specified key.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="key">The approval memory key.</param>
    /// <returns>The remembered approval decision when found; otherwise <see langword="null"/>.</returns>
    ApprovalMemoryEntry? TryGet(string sessionId, string key);

    /// <summary>
    /// Stores a remembered approval for the specified session and key.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="key">The approval memory key.</param>
    /// <param name="entry">The approval memory entry.</param>
    void Store(string sessionId, string key, ApprovalMemoryEntry entry);
}
