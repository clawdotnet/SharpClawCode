using FluentAssertions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies that <see cref="ConversationHistoryAssembler"/> correctly maps session
/// runtime events to ordered <see cref="ChatMessage"/> pairs.
/// </summary>
public sealed class ConversationHistoryAssemblerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static ConversationTurn MakeTurn(string turnId, string input, string? output = null) =>
        new(
            Id: turnId,
            SessionId: "session-1",
            SequenceNumber: 1,
            Input: input,
            Output: output,
            StartedAtUtc: Now,
            CompletedAtUtc: Now,
            AgentId: null,
            SlashCommandName: null,
            Usage: null,
            Metadata: null);

    private static TurnStartedEvent MakeStarted(string turnId, string input) =>
        new(
            EventId: $"evt-started-{turnId}",
            SessionId: "session-1",
            TurnId: turnId,
            OccurredAtUtc: Now,
            Turn: MakeTurn(turnId, input));

    private static TurnCompletedEvent MakeCompleted(string turnId, string input, string summary, string? output = null) =>
        new(
            EventId: $"evt-completed-{turnId}",
            SessionId: "session-1",
            TurnId: turnId,
            OccurredAtUtc: Now,
            Turn: MakeTurn(turnId, input, output ?? summary),
            Succeeded: true,
            Summary: summary);

    // ── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Returns_empty_for_no_events()
    {
        var result = ConversationHistoryAssembler.Assemble([]);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Skips_incomplete_turns()
    {
        // Only a started event, no completed event.
        var events = new RuntimeEvent[]
        {
            MakeStarted("turn-1", "Hello"),
        };

        var result = ConversationHistoryAssembler.Assemble(events);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Assembles_user_assistant_pairs_from_completed_turns()
    {
        var events = new RuntimeEvent[]
        {
            MakeStarted("turn-1", "What is 2+2?"),
            MakeCompleted("turn-1", "What is 2+2?", "The answer is 4."),
            MakeStarted("turn-2", "What about 3+3?"),
            MakeCompleted("turn-2", "What about 3+3?", "The answer is 6."),
        };

        var result = ConversationHistoryAssembler.Assemble(events);

        result.Should().HaveCount(4);

        result[0].Role.Should().Be("user");
        result[0].Content.Should().ContainSingle(b => b.Text == "What is 2+2?");

        result[1].Role.Should().Be("assistant");
        result[1].Content.Should().ContainSingle(b => b.Text == "The answer is 4.");

        result[2].Role.Should().Be("user");
        result[2].Content.Should().ContainSingle(b => b.Text == "What about 3+3?");

        result[3].Role.Should().Be("assistant");
        result[3].Content.Should().ContainSingle(b => b.Text == "The answer is 6.");
    }

    [Fact]
    public void Uses_provider_deltas_when_summary_is_null()
    {
        var completedNoSummary = new TurnCompletedEvent(
            EventId: "evt-completed-turn-1",
            SessionId: "session-1",
            TurnId: "turn-1",
            OccurredAtUtc: Now,
            Turn: MakeTurn("turn-1", "Hello"),
            Succeeded: true,
            Summary: null);

        var delta1 = new ProviderDeltaEvent(
            EventId: "evt-delta-1",
            SessionId: "session-1",
            TurnId: "turn-1",
            OccurredAtUtc: Now,
            ProviderName: "test-provider",
            Model: "test-model",
            ProviderEventId: "p1",
            Kind: "text",
            Content: "Hello ");

        var delta2 = new ProviderDeltaEvent(
            EventId: "evt-delta-2",
            SessionId: "session-1",
            TurnId: "turn-1",
            OccurredAtUtc: Now,
            ProviderName: "test-provider",
            Model: "test-model",
            ProviderEventId: "p2",
            Kind: "text",
            Content: "world.");

        var events = new RuntimeEvent[]
        {
            MakeStarted("turn-1", "Hello"),
            delta1,
            delta2,
            completedNoSummary,
        };

        var result = ConversationHistoryAssembler.Assemble(events);

        result.Should().HaveCount(2);
        result[1].Role.Should().Be("assistant");
        result[1].Content.Should().ContainSingle(b => b.Text == "Hello world.");
    }

    [Fact]
    public void Prefers_persisted_turn_output_over_summary_text()
    {
        var events = new RuntimeEvent[]
        {
            MakeStarted("turn-1", "Summarize the run"),
            MakeCompleted("turn-1", "Summarize the run", "Short summary", "Actual assistant output"),
        };

        var result = ConversationHistoryAssembler.Assemble(events);

        result.Should().HaveCount(2);
        result[1].Role.Should().Be("assistant");
        result[1].Content.Should().ContainSingle(b => b.Text == "Actual assistant output");
    }

    [Fact]
    public void Skips_events_without_turn_id()
    {
        // Session-level event with no TurnId should be silently ignored.
        var sessionCreated = new SessionCreatedEvent(
            EventId: "evt-session",
            SessionId: "session-1",
            TurnId: null,
            OccurredAtUtc: Now,
            Session: new ConversationSession(
                Id: "session-1",
                Title: "Test",
                State: SessionLifecycleState.Active,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                WorkingDirectory: ".",
                RepositoryRoot: null,
                CreatedAtUtc: Now,
                UpdatedAtUtc: Now,
                ActiveTurnId: null,
                LastCheckpointId: null,
                Metadata: null));

        var events = new RuntimeEvent[] { sessionCreated };

        var result = ConversationHistoryAssembler.Assemble(events);

        result.Should().BeEmpty();
    }
}
