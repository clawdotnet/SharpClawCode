using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Turns;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Executes the core work for a single conversation turn.
/// </summary>
public interface ITurnRunner
{
    /// <summary>
    /// Runs a single turn.
    /// </summary>
    /// <param name="session">The parent session.</param>
    /// <param name="turn">The turn being executed.</param>
    /// <param name="request">The prompt request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The turn run result.</returns>
    Task<TurnRunResult> RunAsync(ConversationSession session, ConversationTurn turn, RunPromptRequest request, CancellationToken cancellationToken);
}
