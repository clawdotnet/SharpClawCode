using System.Globalization;
using Anthropic.Models.Messages;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Internal;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Behavior-level tests for MEAI and Anthropic SDK stream adapters (no outbound HTTP).
/// </summary>
public sealed class ProviderStreamAdapterTests
{
    [Fact]
    public async Task OpenAi_meai_adapter_emits_text_deltas_then_completed()
    {
        var clock = new FixedSystemClock();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(" world")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in OpenAiMeaiStreamAdapter.AdaptAsync(Stream(), "req-openai", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(3);
        events.Take(2).Should().OnlyContain(e => e.Kind == "delta" && !e.IsTerminal);
        events.Take(2).Select(e => e.Content).Should().ContainInOrder("Hello", " world");
        events[^1].Kind.Should().Be("completed");
        events[^1].IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAi_meai_adapter_maps_usage_on_terminal_update_when_present()
    {
        var clock = new FixedSystemClock();
        var details = new UsageDetails
        {
            InputTokenCount = 10,
            OutputTokenCount = 20,
            TotalTokenCount = 30,
            CachedInputTokenCount = 1,
        };

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]);
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(details)])
            {
                FinishReason = ChatFinishReason.Stop,
            };
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in OpenAiMeaiStreamAdapter.AdaptAsync(Stream(), "req-usage", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events[^1].Usage.Should().NotBeNull();
        events[^1].Usage!.InputTokens.Should().Be(10);
        events[^1].Usage!.OutputTokens.Should().Be(20);
        events[^1].Usage!.TotalTokens.Should().Be(30);
        events[^1].Usage!.CachedInputTokens.Should().Be(1);
    }

    [Fact]
    public async Task Anthropic_sdk_adapter_maps_content_block_deltas_and_stop()
    {
        var clock = new FixedSystemClock();

        async IAsyncEnumerable<RawMessageStreamEvent> Stream()
        {
            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 0,
                    Delta = new RawContentBlockDelta(new TextDelta("Hello"), null),
                },
                default);

            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 0,
                    Delta = new RawContentBlockDelta(new TextDelta(" world"), null),
                },
                default);

            yield return new RawMessageStreamEvent(new RawMessageStopEvent(), default);
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in AnthropicSdkStreamAdapter.AdaptAsync(Stream(), "req-anthropic", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(3);
        events.Take(2).Should().OnlyContain(e => e.Kind == "delta" && !e.IsTerminal);
        events.Take(2).Select(e => e.Content).Should().ContainInOrder("Hello", " world");
        events[^1].Kind.Should().Be("completed");
        events[^1].IsTerminal.Should().BeTrue();
    }

    private sealed class FixedSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-04-06T00:00:00Z", CultureInfo.InvariantCulture);
    }
}
