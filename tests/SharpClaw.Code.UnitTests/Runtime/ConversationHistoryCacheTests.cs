using System.Collections;
using System.Reflection;
using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies the in-process conversation history cache evicts by recency rather than key ordering.
/// </summary>
public sealed class ConversationHistoryCacheTests
{
    private static readonly Type CacheType = typeof(PromptContextAssembler).Assembly
        .GetType("SharpClaw.Code.Runtime.Context.ConversationHistoryCache")
        ?? throw new InvalidOperationException("ConversationHistoryCache type was not found.");

    [Fact]
    public void Store_and_TryGet_keep_recently_touched_entries_when_cache_overflows()
    {
        ResetCache();

        var history = CreateHistory("cached");
        for (var index = 0; index < 100; index++)
        {
            Store($"workspace-{index}", $"session-{index}", 1, history);
        }

        TryGet("workspace-0", "session-0", 1).Should().BeTrue();

        Store("workspace-100", "session-100", 1, history);

        var keys = GetCacheKeys();
        keys.Should().HaveCount(100);
        keys.Should().Contain("workspace-0\0session-0");
        keys.Should().NotContain("workspace-1\0session-1");
    }

    private static void Store(string workspaceRoot, string sessionId, int completedTurnSequence, IReadOnlyList<ChatMessage> history)
        => CacheType.GetMethod("Store", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, [workspaceRoot, sessionId, completedTurnSequence, history]);

    private static bool TryGet(string workspaceRoot, string sessionId, int completedTurnSequence)
    {
        var arguments = new object?[] { workspaceRoot, sessionId, completedTurnSequence, null };
        var hit = (bool)CacheType.GetMethod("TryGet", BindingFlags.Public | BindingFlags.Static)!
            .Invoke(null, arguments)!;
        arguments[3].Should().NotBeNull();
        return hit;
    }

    private static string[] GetCacheKeys()
    {
        var cache = GetCacheDictionary();
        return cache.Keys.Cast<object>().Select(static key => (string)key).ToArray();
    }

    private static IDictionary GetCacheDictionary()
        => (IDictionary)(CacheType.GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)
            ?? throw new InvalidOperationException("Conversation history cache instance was null."));

    private static void ResetCache()
    {
        GetCacheDictionary().Clear();
        CacheType.GetField("accessCounter", BindingFlags.NonPublic | BindingFlags.Static)!
            .SetValue(null, 0L);
    }

    private static ChatMessage[] CreateHistory(string text)
        =>
        [
            new ChatMessage("assistant", [new ContentBlock(ContentBlockKind.Text, text, null, null, null, null)])
        ];
}
