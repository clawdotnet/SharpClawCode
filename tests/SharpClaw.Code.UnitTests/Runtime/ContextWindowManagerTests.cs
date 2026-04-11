using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies that <see cref="ContextWindowManager"/> correctly trims conversation history
/// to satisfy a token budget.
/// </summary>
public sealed class ContextWindowManagerTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ChatMessage UserMessage(string text) =>
        new("user", [new ContentBlock(ContentBlockKind.Text, text, null, null, null, null)]);

    private static ChatMessage AssistantMessage(string text) =>
        new("assistant", [new ContentBlock(ContentBlockKind.Text, text, null, null, null, null)]);

    private static ChatMessage SystemMessage(string text) =>
        new("system", [new ContentBlock(ContentBlockKind.Text, text, null, null, null, null)]);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_all_when_within_budget()
    {
        var messages = new ChatMessage[]
        {
            UserMessage("Hello"),
            AssistantMessage("Hi there"),
            UserMessage("How are you?"),
            AssistantMessage("I am well."),
        };

        // Budget is very generous — all messages should be returned.
        var result = ContextWindowManager.Truncate(messages, 10_000);

        result.Should().HaveCount(4);
    }

    [Fact]
    public void Truncates_oldest_messages_when_over_budget()
    {
        // Each message has ~400 chars → ~100 tokens.
        // 10 messages → ~1000 tokens total. Budget: 500 tokens → expect roughly half.
        var filler = new string('x', 400);
        var messages = Enumerable.Range(0, 10)
            .Select(i => i % 2 == 0 ? UserMessage($"{i}: {filler}") : AssistantMessage($"{i}: {filler}"))
            .ToArray();

        var result = ContextWindowManager.Truncate(messages, 500);

        // Should keep fewer messages than the original.
        result.Length.Should().BeLessThan(messages.Length);

        // The most recent message must be present.
        result[^1].Should().Be(messages[^1]);
    }

    [Fact]
    public void Always_keeps_system_message()
    {
        var systemMsg = SystemMessage("You are a helpful assistant.");
        var filler = new string('x', 2000); // ~500 tokens each

        var messages = new ChatMessage[]
        {
            systemMsg,
            UserMessage(filler),
            AssistantMessage(filler),
            UserMessage(filler),
            AssistantMessage(filler),
        };

        // Very tight budget — only slightly above system message cost.
        var systemTokens = systemMsg.Content.Sum(b => (b.Text?.Length ?? 0)) / 4;
        var budget = systemTokens + 10; // small extra room

        var result = ContextWindowManager.Truncate(messages, budget);

        result.Should().Contain(m => m.Role == "system");
    }

    [Fact]
    public void Always_keeps_most_recent_message_even_under_extreme_budget()
    {
        var messages = new ChatMessage[]
        {
            UserMessage(new string('a', 4000)),
            AssistantMessage(new string('b', 4000)),
            UserMessage("final question"),
        };

        // Budget too small for anything substantial.
        var result = ContextWindowManager.Truncate(messages, 1);

        result.Should().NotBeEmpty();
        result[^1].Should().Be(messages[^1]);
    }

    [Fact]
    public void Returns_empty_for_empty_input()
    {
        var result = ContextWindowManager.Truncate([], 1000);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Throws_for_non_positive_budget()
    {
        var act = () => ContextWindowManager.Truncate([UserMessage("hi")], 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
