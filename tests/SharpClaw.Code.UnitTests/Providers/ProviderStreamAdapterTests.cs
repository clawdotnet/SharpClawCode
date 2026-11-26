using System.Globalization;
using System.Text.Json;
using Anthropic.Models.Messages;
using FluentAssertions;
using Microsoft.Extensions.AI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Internal;
using SharpClaw.Code.Providers.Models;
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
    public async Task OpenAi_meai_adapter_keeps_reading_after_finish_reason_for_tool_calls_and_usage()
    {
        // MEAI emits the finish chunk first, then usage, then the aggregated function call after the SSE ends.
        var clock = new FixedSystemClock();
        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ToolCalls };
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new UsageContent(new UsageDetails { InputTokenCount = 7, OutputTokenCount = 3, TotalTokenCount = 10 })])
            {
                FinishReason = ChatFinishReason.ToolCalls,
            };
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("call-1", "read_file", new Dictionary<string, object?> { ["path"] = "a.txt" })])
            {
                FinishReason = ChatFinishReason.ToolCalls,
            };
            await Task.CompletedTask;
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in OpenAiMeaiStreamAdapter.AdaptAsync(Stream(), "req-tools", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().ContainSingle(e => e.Kind == "tool_use" && e.ToolUseId == "call-1" && e.ToolName == "read_file");
        events.Count(e => e.IsTerminal).Should().Be(1);
        events[^1].Kind.Should().Be("completed");
        events[^1].Usage!.TotalTokens.Should().Be(10);
    }

    [Fact]
    public async Task OpenAi_meai_adapter_emits_failed_event_when_sse_data_is_malformed()
    {
        var clock = new FixedSystemClock();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            await Task.Yield();
            throw new JsonException("Malformed SSE data payload.");
#pragma warning disable CS0162
            yield return new ChatResponseUpdate(ChatRole.Assistant, []);
#pragma warning restore CS0162
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in OpenAiMeaiStreamAdapter.AdaptAsync(Stream(), "req-openai-bad-sse", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().ContainSingle();
        events[0].Kind.Should().Be("failed");
        events[0].IsTerminal.Should().BeTrue();
        events[0].Content.Should().Contain("Malformed SSE data payload");
    }

    [Fact]
    public async Task OpenAi_meai_adapter_preserves_cancellation_when_stream_reports_transport_error_after_cancel()
    {
        var clock = new FixedSystemClock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            await Task.Yield();
            throw new IOException("socket closed while reading SSE data.");
#pragma warning disable CS0162
            yield return new ChatResponseUpdate(ChatRole.Assistant, []);
#pragma warning restore CS0162
        }

        var act = async () =>
        {
            await foreach (var _ in OpenAiMeaiStreamAdapter.AdaptAsync(Stream(), "req-openai-cancel", clock, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ProviderStreamFailureClassifier_preserves_reflected_http_status_for_auth_failures()
    {
        var exception = new ProviderStatusException(System.Net.HttpStatusCode.Unauthorized, "token expired");
        var failedEvent = new ProviderEvent(
            "provider-event-auth",
            "req-auth",
            "failed",
            DateTimeOffset.UtcNow,
            ProviderStreamFailureClassifier.Describe(exception),
            true,
            null);

        ProviderStreamFailureClassifier.IsAuthenticationFailure(exception).Should().BeTrue();
        failedEvent.Content.Should().StartWith("HTTP 401 Unauthorized:");
        ProviderStreamFailureClassifier.ClassifyFailedEvent(failedEvent).Should().Be(ProviderFailureKind.AuthenticationUnavailable);
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

    [Fact]
    public async Task Anthropic_sdk_adapter_emits_failed_event_when_sse_data_is_malformed()
    {
        var clock = new FixedSystemClock();

        async IAsyncEnumerable<RawMessageStreamEvent> Stream()
        {
            await Task.Yield();
            throw new JsonException("Malformed Anthropic SSE data payload.");
#pragma warning disable CS0162
            yield return new RawMessageStreamEvent(new RawMessageStopEvent(), default);
#pragma warning restore CS0162
        }

        var events = new List<ProviderEvent>();
        await foreach (var e in AnthropicSdkStreamAdapter.AdaptAsync(Stream(), "req-anthropic-bad-sse", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().ContainSingle();
        events[0].Kind.Should().Be("failed");
        events[0].IsTerminal.Should().BeTrue();
        events[0].Content.Should().Contain("Malformed Anthropic SSE data payload");
    }

    [Fact]
    public async Task Anthropic_sdk_adapter_preserves_cancellation_when_stream_reports_transport_error_after_cancel()
    {
        var clock = new FixedSystemClock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        async IAsyncEnumerable<RawMessageStreamEvent> Stream()
        {
            await Task.Yield();
            throw new IOException("socket closed while reading Anthropic SSE data.");
#pragma warning disable CS0162
            yield return new RawMessageStreamEvent(new RawMessageStopEvent(), default);
#pragma warning restore CS0162
        }

        var act = async () =>
        {
            await foreach (var _ in AnthropicSdkStreamAdapter.AdaptAsync(Stream(), "req-anthropic-cancel", clock, cts.Token))
            {
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FixedSystemClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-04-06T00:00:00Z", CultureInfo.InvariantCulture);
    }

    private sealed class ProviderStatusException(System.Net.HttpStatusCode statusCode, string message) : Exception(message)
    {
        public System.Net.HttpStatusCode StatusCode { get; } = statusCode;
    }
}
