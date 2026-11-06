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
    /// Completed turns contribute their conversation; interrupted turns retain accepted user corrections
    /// and their original request without inventing an assistant response.
    /// </summary>
    /// <param name="events">The full ordered event log for a session.</param>
    /// <returns>
    /// An array of <see cref="ChatMessage"/> objects in chronological turn order,
    /// incorporating the latest completed primary compaction snapshot and subsequent steering.
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

            if (started is null) continue;
            if (completed is null)
            {
                // Accepted steering is durable even when cancellation prevents a final answer.
                // An unfinished compaction must not replace history from completed turns.
                var corrections = bucket.OfType<TurnSteeredEvent>().ToArray();
                if (corrections.Length > 0)
                {
                    messages.Add(new ChatMessage("user", [new(ContentBlockKind.Text, started.Turn.Input ?? string.Empty, null, null, null, null)]));
                    foreach (var correction in corrections)
                        messages.Add(new ChatMessage("user", [new(ContentBlockKind.Text, correction.Message, null, null, null, null)]));
                }
                continue;
            }

            // User message: the raw input for the turn.
            var userInput = started.Turn.Input ?? string.Empty;
            var compaction = bucket.OfType<ContextCompactedEvent>().LastOrDefault(item => item.IsPrimaryRun);
            if (compaction is not null)
            {
                messages.Clear();
                messages.AddRange(compaction.Messages);
            }
            else messages.Add(new ChatMessage(
                "user",
                [new ContentBlock(ContentBlockKind.Text, userInput, null, null, null, null)]));

            var replayEvents = compaction is null ? bucket : bucket.Skip(bucket.IndexOf(compaction) + 1);
            foreach (var correction in replayEvents.OfType<TurnSteeredEvent>())
                messages.Add(new ChatMessage("user", [new(ContentBlockKind.Text, correction.Message, null, null, null, null)]));

            // Assistant message: prefer the persisted turn output; fall back to accumulated deltas.
            string assistantText;
            if (!string.IsNullOrWhiteSpace(completed.Turn.Output))
            {
                assistantText = completed.Turn.Output!;
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
