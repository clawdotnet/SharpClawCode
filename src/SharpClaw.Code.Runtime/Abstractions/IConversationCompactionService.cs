using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Produces compact session summaries that can be reused in future turns.
/// </summary>
public interface IConversationCompactionService
{
    /// <summary>
    /// Compacts the session into a reusable summary and optional updated title.
    /// </summary>
    Task<(ConversationSession Session, string Summary)> CompactAsync(
        string workspaceRoot,
        string sessionId,
        CancellationToken cancellationToken);
}
