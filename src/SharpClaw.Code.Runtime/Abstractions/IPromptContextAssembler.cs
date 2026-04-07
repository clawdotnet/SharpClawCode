using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Assembles supplemental prompt context from workspace and session services.
/// </summary>
public interface IPromptContextAssembler
{
    /// <summary>
    /// Builds an enriched prompt payload for the current run.
    /// </summary>
    /// <param name="session">The active conversation session.</param>
    /// <param name="turn">The turn being executed.</param>
    /// <param name="request">The current prompt request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The enriched prompt payload.</returns>
    Task<PromptExecutionContext> AssembleAsync(
        ConversationSession session,
        ConversationTurn turn,
        RunPromptRequest request,
        CancellationToken cancellationToken);
}
