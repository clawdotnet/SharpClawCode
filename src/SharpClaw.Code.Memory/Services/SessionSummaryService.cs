using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Produces a compact session summary from persisted session state.
/// </summary>
public sealed class SessionSummaryService : ISessionSummaryService
{
    /// <inheritdoc />
    public Task<string?> BuildSummaryAsync(ConversationSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var fragments = new List<string>
        {
            $"State: {session.State}.",
            $"Permission mode: {session.PermissionMode}.",
            $"Output format: {session.OutputFormat}."
        };

        if (!string.IsNullOrWhiteSpace(session.LastCheckpointId))
        {
            fragments.Add($"Last checkpoint: {session.LastCheckpointId}.");
        }

        if (session.Metadata is not null && session.Metadata.TryGetValue("lastTurnId", out var lastTurnId))
        {
            fragments.Add($"Last turn: {lastTurnId}.");
        }

        return Task.FromResult<string?>(string.Join(" ", fragments));
    }
}
