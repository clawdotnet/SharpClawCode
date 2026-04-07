using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Coordinates conversation sessions, turn execution, and durable state.
/// </summary>
public interface IConversationRuntime
{
    /// <summary>
    /// Creates a new durable conversation session.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="permissionMode">The permission mode to apply.</param>
    /// <param name="outputFormat">The preferred output format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created conversation session.</returns>
    Task<ConversationSession> CreateSessionAsync(string workspacePath, PermissionMode permissionMode, OutputFormat outputFormat, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a session snapshot by id.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The session snapshot, if found.</returns>
    Task<ConversationSession?> GetSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest session snapshot for a workspace.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest session snapshot, if any.</returns>
    Task<ConversationSession?> GetLatestSessionAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a prompt through the conversation runtime.
    /// </summary>
    /// <param name="request">The prompt request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The turn execution result.</returns>
    Task<TurnExecutionResult> RunPromptAsync(RunPromptRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a child session linked to an existing session without copying the full event log.
    /// </summary>
    /// <param name="workspacePath">Workspace root path.</param>
    /// <param name="sourceSessionId">Parent session id, or null to fork the latest session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The new child session snapshot.</returns>
    Task<ConversationSession> ForkSessionAsync(string workspacePath, string? sourceSessionId, CancellationToken cancellationToken);
}
