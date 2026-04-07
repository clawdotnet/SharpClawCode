using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Persists durable conversation session snapshots.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Saves a conversation session snapshot.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="session">The session snapshot.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a session snapshot by id.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session snapshot, if found.</returns>
    Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest session snapshot for a workspace.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest session snapshot, if any.</returns>
    Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all persisted sessions for a workspace ordered by <see cref="ConversationSession.UpdatedAtUtc"/> descending.
    /// </summary>
    Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken);
}
