using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Trims conversation history to fit within a token budget using a simple
/// character-based token estimate (characters ÷ 4).
/// </summary>
public static class ContextWindowManager
{
    private const int CharsPerTokenEstimate = 4;

    /// <summary>
    /// Returns a subset of <paramref name="messages"/> that fits within
    /// <paramref name="maxTokenBudget"/> estimated tokens.
    ///
    /// Rules applied in priority order:
    /// <list type="number">
    ///   <item>The system message (role == "system") is always kept.</item>
    ///   <item>The most-recent non-system message is always kept.</item>
    ///   <item>Oldest non-system messages are dropped until the budget is satisfied.</item>
    /// </list>
    /// </summary>
    /// <param name="messages">The full conversation history to truncate.</param>
    /// <param name="maxTokenBudget">The maximum number of estimated tokens to allow.</param>
    /// <returns>A (possibly shorter) ordered array of <see cref="ChatMessage"/> objects.</returns>
    public static ChatMessage[] Truncate(IReadOnlyList<ChatMessage> messages, int maxTokenBudget)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (maxTokenBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTokenBudget), "Token budget must be positive.");
        }

        if (messages.Count == 0)
        {
            return [];
        }

        // Fast path: everything fits already.
        if (EstimateTokens(messages) <= maxTokenBudget)
        {
            return [.. messages];
        }

        // Separate the system message from the rest.
        var systemMessage = messages.FirstOrDefault(m =>
            string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));

        // Build a list of non-system messages with precomputed token estimates.
        var working = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var systemTokens = systemMessage is not null ? EstimateTokens(systemMessage) : 0;
        var runningTotal = working.Sum(EstimateTokens) + systemTokens;

        // Drop oldest messages until budget is satisfied (O(n) via running total).
        // Always preserve at least the last message (most recent non-system turn).
        var dropIndex = 0;
        while (dropIndex < working.Count - 1 && runningTotal > maxTokenBudget)
        {
            runningTotal -= EstimateTokens(working[dropIndex]);
            dropIndex++;
        }

        if (dropIndex > 0)
        {
            working = working.GetRange(dropIndex, working.Count - dropIndex);
        }

        // Reassemble with system message first (if present).
        var result = new List<ChatMessage>(working.Count + 1);
        if (systemMessage is not null)
        {
            result.Add(systemMessage);
        }

        result.AddRange(working);
        return [.. result];
    }

    private static int EstimateTokens(IEnumerable<ChatMessage> messages)
        => messages.Sum(EstimateTokens);

    private static int EstimateTokens(ChatMessage message)
    {
        var charCount = message.Content.Sum(block =>
            (block.Text?.Length ?? 0)
            + (block.ToolName?.Length ?? 0)
            + (block.ToolInputJson?.Length ?? 0));

        return (charCount + CharsPerTokenEstimate - 1) / CharsPerTokenEstimate;
    }
}
