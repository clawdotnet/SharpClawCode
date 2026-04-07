using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Produces a compact session summary suitable for prompt context.
/// </summary>
public interface ISessionSummaryService
{
    /// <summary>
    /// Builds a concise summary for the current session state.
    /// </summary>
    /// <param name="session">The active conversation session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The summary text, if available.</returns>
    Task<string?> BuildSummaryAsync(ConversationSession session, CancellationToken cancellationToken);
}
