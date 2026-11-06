using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class AutomaticContextManagerTests
{
    private readonly Store store = new();
    private readonly SummaryProvider provider = new();
    private readonly AutomaticCompactionOptions settings = new()
    { ContextWindowTokens = 20_000, ReservedOutputTokens = 256, SummaryMaxTokens = 512, RetainedMessageCount = 2, TriggerRatio = 0.25 };
    private static readonly CompactionWorkingState State = new("Fix parser", ["Keep public API stable"], ["Use exact symbols"], ["Parser.cs"], ["Unit tests passed"], ["Run integration tests"]);

    [Fact]
    public async Task Compaction_preserves_tool_pairs_and_current_request_and_roundtrips_durably()
    {
        var input = Request();
        var result = await Manager().PrepareAsync(Context(), provider, input, CancellationToken.None);
        provider.Request!.Tools.Should().BeNull();
        provider.Request.MaxTokens.Should().Be(settings.SummaryMaxTokens);
        var evt = store.Events.Should().ContainSingle().Subject.Should().BeOfType<ContextCompactedEvent>().Subject;
        evt.ArchivedMessages.Should().HaveCount(3);
        evt.Messages.Should().HaveCount(4);
        evt.Messages[1].Content[0].Text.Should().Be("Current request: preserve behavior.");
        evt.Messages[2].Content[0].Kind.Should().Be(ContentBlockKind.ToolUse);
        evt.Messages[3].Content[0].Kind.Should().Be(ContentBlockKind.ToolResult);
        result.Request.Messages.Should().BeEquivalentTo(evt.Messages);
        evt.EstimatedTokensAfter.Should().BeLessThan(evt.EstimatedTokensBefore);
        var json = JsonSerializer.Serialize<RuntimeEvent>(evt, ProtocolJsonContext.Default.RuntimeEvent);
        var restored = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.RuntimeEvent).Should().BeOfType<ContextCompactedEvent>().Subject;
        restored.WorkingState.Constraints.Should().Contain("Keep public API stable");
        restored.ArchivedMessages[1].Content[0].Text.Should().HaveLength(32_000);
        var turn = new ConversationTurn("turn", "session", 1, "Original request", "Final answer", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null);
        var replay = ConversationHistoryAssembler.Assemble([
            new TurnStartedEvent("start", "session", "turn", DateTimeOffset.UtcNow, turn), restored,
            new TurnCompletedEvent("end", "session", "turn", DateTimeOffset.UtcNow, turn, true, null)]);
        replay.Should().HaveCount(5);
        replay[0].Content[0].Text.Should().Contain("Keep public API stable");
        replay[^1].Content[0].Text.Should().Be("Final answer");
    }

    [Fact]
    public async Task Small_requests_do_not_invoke_provider_or_write_events()
    {
        var request = Request() with { Messages = [Text("short")] };
        (await Manager().PrepareAsync(Context(), provider, request, CancellationToken.None)).Request.Should().BeSameAs(request);
        provider.Request.Should().BeNull();
        store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Images_are_retained_verbatim_and_not_sent_as_base64_to_the_summarizer()
    {
        var image = new ContentBlock(ContentBlockKind.Image, null, null, null, null, null, "image/png", "opaque-image-data");
        var request = Request();
        var messages = request.Messages!;
        request = request with { Messages = messages.Take(1).Concat([new ChatMessage("user", [image])]).Concat(messages.Skip(1)).ToArray(), ContainsImageInput = true };
        var result = await Manager().PrepareAsync(Context(), provider, request, CancellationToken.None);
        result.Request.Messages!.SelectMany(message => message.Content).Should().Contain(image);
        result.Request.ContainsImageInput.Should().BeTrue();
        provider.Request!.ContainsImageInput.Should().BeFalse();
        provider.Request.Prompt.Should().NotContain("opaque-image-data");
        store.Events.OfType<ContextCompactedEvent>().Single().ArchivedMessages.SelectMany(message => message.Content).Should().Contain(image);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    public async Task Invalid_summary_never_replaces_context_or_appends_events(string response)
    {
        provider.Response = response;
        var request = Request();
        var original = request.Messages!.ToArray();
        await Assert.ThrowsAnyAsync<Exception>(() => Manager().PrepareAsync(Context(), provider, request, CancellationToken.None));
        request.Messages.Should().Equal(original);
        store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Persistence_failure_is_fatal_before_replacement()
    {
        store.Fail = true;
        var request = Request();
        await Assert.ThrowsAsync<IOException>(() => Manager().PrepareAsync(Context(), provider, request, CancellationToken.None));
        request.Messages.Should().HaveCount(5);
        store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_during_summary_does_not_publish_a_compaction()
    {
        using var cts = new CancellationTokenSource();
        provider.DuringStream = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Manager().PrepareAsync(Context(), provider, Request(), cts.Token));
        store.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Model_window_override_and_output_reservation_are_enforced()
    {
        settings.ModelContextWindows["test/small"] = 500;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Manager().PrepareAsync(Context(), provider, Request() with { Model = "small" }, CancellationToken.None));
        provider.Request.Should().BeNull();
    }

    [Fact]
    public async Task Small_window_does_not_fail_requests_below_the_compaction_trigger()
    {
        settings.ModelContextWindows["test/small"] = 1_000;
        var request = Request() with { Model = "small", Messages = [Text("short")] };
        (await Manager().PrepareAsync(Context(), provider, request, CancellationToken.None)).Request.Should().BeSameAs(request);
        provider.Request.Should().BeNull();
    }

    [Fact]
    public async Task Original_request_stays_verbatim_after_a_steering_correction()
    {
        var messages = Request().Messages!.Concat([
            Text("Correction: keep tabs."),
            new("assistant", [new(ContentBlockKind.ToolUse, null, "call2", "read_file", "{}", null)]),
            new("user", [new(ContentBlockKind.ToolResult, "more contents", "call2", null, null, false)])]).ToArray();
        var result = await Manager().PrepareAsync(Context(), provider, Request() with { Messages = messages }, CancellationToken.None);
        var texts = result.Request.Messages!.SelectMany(message => message.Content).Select(block => block.Text).ToArray();
        texts.Should().Contain("Current request: preserve behavior.").And.Contain("Correction: keep tabs.");
        store.Events.OfType<ContextCompactedEvent>().Should().ContainSingle();
    }

    [Fact]
    public void Unfinished_or_child_compaction_does_not_replace_primary_replay()
    {
        var turn = new ConversationTurn("turn", "session", 1, "original", "answer", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null, null);
        var evt = new ContextCompactedEvent("compact", "session", "turn", DateTimeOffset.UtcNow, false, "model", State, [], [Text("child only")], 100, 20, null);
        var started = new TurnStartedEvent("start", "session", "turn", DateTimeOffset.UtcNow, turn);
        ConversationHistoryAssembler.Assemble([started, evt]).Should().BeEmpty();
        ConversationHistoryAssembler.Assemble([started, evt with { IsPrimaryRun = true }]).Should().BeEmpty();
        var replay = ConversationHistoryAssembler.Assemble([started, evt, new TurnCompletedEvent("end", "session", "turn", DateTimeOffset.UtcNow, turn, true, null)]);
        replay[0].Content[0].Text.Should().Be("original");
    }

    private AutomaticContextManager Manager() => new(Options.Create(settings), store, new RuntimeEventPublisher(Options.Create(new TelemetryOptions()), new UsageTracker()));
    private static AgentRunContext Context() => new("session", "turn", "Current request: preserve behavior.", Path.GetTempPath(), "model", PermissionMode.ReadOnly, OutputFormat.Text, null!, null);
    private static ProviderRequest Request() => new("request", "session", "turn", "test", "model", "Current request: preserve behavior.", "system", OutputFormat.Text, 0, null,
        [Text("Keep public API stable"), Text(new string('x', 32_000), "assistant"), Text("Current request: preserve behavior."),
            new("assistant", [new(ContentBlockKind.ToolUse, null, "call1", "read_file", "{}", null)]),
            new("user", [new(ContentBlockKind.ToolResult, "file contents", "call1", null, null, false)])], MaxTokens: 256);
    private static ChatMessage Text(string text, string role = "user") => new(role, [new(ContentBlockKind.Text, text, null, null, null, null)]);
    private sealed class Store : IEventStore
    {
        public bool Fail { get; set; }
        public List<RuntimeEvent> Events { get; } = [];
        public Task AppendAsync(string path, string session, RuntimeEvent evt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("Persistence unavailable");
            Events.Add(evt);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string path, string session, CancellationToken token) => Task.FromResult<IReadOnlyList<RuntimeEvent>>(Events);
    }
    private sealed class SummaryProvider : IModelProvider
    {
        public string ProviderName => "test";
        public string Response { get; set; } = JsonSerializer.Serialize(State, ProtocolJsonContext.Default.CompactionWorkingState);
        public ProviderRequest? Request { get; private set; }
        public Action? DuringStream { get; set; }
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken token)
        {
            Request = request;
            return Task.FromResult(new ProviderStreamHandle(request, Stream(request, token)));
        }
        private async IAsyncEnumerable<ProviderEvent> Stream(ProviderRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            yield return new("delta", request.Id, "delta", DateTimeOffset.UtcNow, Response, false, null);
            await Task.Yield();
            DuringStream?.Invoke();
            token.ThrowIfCancellationRequested();
            yield return new("done", request.Id, "done", DateTimeOffset.UtcNow, null, true, new(100, 20, 0, 120, null));
        }
    }
}
