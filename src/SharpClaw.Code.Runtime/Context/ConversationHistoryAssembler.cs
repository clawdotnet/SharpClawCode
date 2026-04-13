using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Assembles <see cref="ChatMessage"/> conversation history from a session's persisted runtime events.
/// </summary>
public static class ConversationHistoryAssembler
{
    /// <summary>
    /// Converts a flat list of runtime events into an ordered conversation history.
    /// Only completed turns (those with both a <see cref="TurnStartedEvent"/> and a
    /// <see cref="TurnCompletedEvent"/>) contribute to the returned messages.
    /// </summary>
    /// <param name="events">The full ordered event log for a session.</param>
    /// <returns>
    /// An array of <see cref="ChatMessage"/> objects in chronological turn order,
    /// alternating user / assistant pairs.
    /// </returns>
    public static ChatMessage[] Assemble(IReadOnlyList<RuntimeEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return [];
        }

        // Group events by TurnId, preserving insertion order of first occurrence.
        var turnOrder = new List<string>();
        var turnEvents = new Dictionary<string, List<RuntimeEvent>>(StringComparer.Ordinal);

        foreach (var evt in events)
        {
            if (evt.TurnId is null)
            {
                continue;
            }

            if (!turnEvents.TryGetValue(evt.TurnId, out var bucket))
            {
                bucket = [];
                turnEvents[evt.TurnId] = bucket;
                turnOrder.Add(evt.TurnId);
            }

            bucket.Add(evt);
        }

        var messages = new List<ChatMessage>(turnOrder.Count * 2);

        foreach (var turnId in turnOrder)
        {
            var bucket = turnEvents[turnId];

            var started = bucket.OfType<TurnStartedEvent>().FirstOrDefault();
            var completed = bucket.OfType<TurnCompletedEvent>().FirstOrDefault();

            // Skip incomplete turns.
            if (started is null || completed is null)
            {
                continue;
            }

            // User message: the raw input for the turn.
            var userInput = started.Turn.Input ?? string.Empty;
            messages.Add(new ChatMessage(
                "user",
                [new ContentBlock(ContentBlockKind.Text, userInput, null, null, null, null)]));

            // Assistant message: prefer the turn summary; fall back to accumulated deltas.
            string assistantText;
            if (!string.IsNullOrWhiteSpace(completed.Summary))
            {
                assistantText = completed.Summary;
            }
            else
            {
                var deltas = bucket.OfType<ProviderDeltaEvent>();
                assistantText = string.Concat(deltas.Select(d => d.Content));
            }

            if (string.IsNullOrEmpty(assistantText))
            {
                assistantText = string.Empty;
            }

            messages.Add(new ChatMessage(
                "assistant",
                [new ContentBlock(ContentBlockKind.Text, assistantText, null, null, null, null)]));
        }

        return [.. messages];
    }
}
