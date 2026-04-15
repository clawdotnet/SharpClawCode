using System.Collections.Concurrent;
using System.Threading;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Caches assembled conversation history per session so follow-up turns can reuse
/// in-process transcript state without rereading the full event log.
/// </summary>
internal static class ConversationHistoryCache
{
    internal const int MaxHistoryTokenBudget = 100_000;
    private const int MaxCacheEntries = 100;
    private static readonly ConcurrentDictionary<string, CacheEntry> Cache = new(StringComparer.Ordinal);
    private static long accessCounter;

    public static bool TryGet(
        string workspaceRoot,
        string sessionId,
        int completedTurnSequence,
        out IReadOnlyList<ChatMessage> history)
    {
        var key = CreateKey(workspaceRoot, sessionId);
        if (completedTurnSequence >= 0
            && Cache.TryGetValue(key, out var entry)
            && entry.CompletedTurnSequence == completedTurnSequence)
        {
            var touchedEntry = entry with { AccessOrder = NextAccessOrder() };
            Cache.TryUpdate(key, touchedEntry, entry);
            history = touchedEntry.History;
            return true;
        }

        history = [];
        return false;
    }

    public static void Store(
        string workspaceRoot,
        string sessionId,
        int completedTurnSequence,
        IReadOnlyList<ChatMessage> history)
    {
        var key = CreateKey(workspaceRoot, sessionId);
        Cache[key] = new CacheEntry(completedTurnSequence, [.. history], NextAccessOrder());
        EvictOverflow();
    }

    public static void StoreCompletedTurn(string workspaceRoot, string sessionId, ConversationTurn completedTurn)
    {
        ArgumentNullException.ThrowIfNull(completedTurn);
        if (completedTurn.SequenceNumber <= 0 || string.IsNullOrWhiteSpace(completedTurn.Output))
        {
            return;
        }

        var previousSequence = completedTurn.SequenceNumber - 1;
        IReadOnlyList<ChatMessage> priorHistory = [];
        if (previousSequence > 0
            && !TryGet(workspaceRoot, sessionId, previousSequence, out priorHistory))
        {
            return;
        }

        var updatedHistory = priorHistory
            .Concat(
            [
                CreateMessage("user", completedTurn.Input),
                CreateMessage("assistant", completedTurn.Output),
            ])
            .ToArray();

        Store(
            workspaceRoot,
            sessionId,
            completedTurn.SequenceNumber,
            ContextWindowManager.Truncate(updatedHistory, MaxHistoryTokenBudget));
    }

    private static ChatMessage CreateMessage(string role, string text)
        => new(role, [new ContentBlock(ContentBlockKind.Text, text, null, null, null, null)]);

    private static string CreateKey(string workspaceRoot, string sessionId)
        => $"{workspaceRoot}\u0000{sessionId}";

    private static void EvictOverflow()
    {
        if (Cache.Count <= MaxCacheEntries)
        {
            return;
        }

        var overflowKeys = Cache
            .OrderBy(static pair => pair.Value.AccessOrder)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(Cache.Count - MaxCacheEntries)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in overflowKeys)
        {
            Cache.TryRemove(key, out _);
        }
    }

    private static long NextAccessOrder() => Interlocked.Increment(ref accessCounter);

    private sealed record CacheEntry(int CompletedTurnSequence, ChatMessage[] History, long AccessOrder);
}
