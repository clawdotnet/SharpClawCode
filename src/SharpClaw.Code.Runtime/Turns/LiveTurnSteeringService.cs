using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>Durably accepts queued corrections and publishes them to the same event sinks as other runtime activity.</summary>
public sealed class LiveTurnSteeringService(LiveTurnControlService controls, IEventStore events, IRuntimeEventPublisher publisher) : IAgentTurnControlAccessor
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ConsumeSteeringAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        if (context.ParentAgentId is not null || controls.Current is not { } handle) return [];
        var messages = new List<string>();
        // Bound this drain even if the caller keeps submitting corrections.
        for (var i = 0; i < 16 && handle.PeekSteering() is { } message; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evt = new TurnSteeredEvent($"event-{Guid.NewGuid():N}", context.SessionId, context.TurnId, DateTimeOffset.UtcNow, message);
            await events.AppendAsync(context.WorkingDirectory, context.SessionId, evt, cancellationToken).ConfigureAwait(false);
            handle.CompleteSteering();
            ConversationHistoryCache.Invalidate(context.WorkingDirectory, context.SessionId);
            await publisher.PublishAsync(evt, new(context.WorkingDirectory, context.SessionId), cancellationToken).ConfigureAwait(false);
            messages.Add(message);
        }
        return messages;
    }
}
