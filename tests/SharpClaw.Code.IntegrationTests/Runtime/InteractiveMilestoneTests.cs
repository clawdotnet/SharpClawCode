using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Configuration;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>Exercises compaction, steering and cancellation through the real provider/tool/session composition.</summary>
public sealed class InteractiveMilestoneTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"sharpclaw-milestone-{Guid.NewGuid():N}");

    /// <summary>Snapshot mutations integrate with durable checkpoint undo/redo across host instances.</summary>
    [Fact]
    public async Task Snapshot_edit_survives_restart_and_uses_existing_undo_redo()
    {
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, "editable.txt");
        await File.WriteAllTextAsync(path, "before\r\n");
        string sessionId;
        using (var services = Services(new ScenarioProvider { SnapshotEdit = true }))
        {
            var result = await services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Edit the file.") with { PermissionMode = PermissionMode.WorkspaceWrite }, CancellationToken.None);
            result.ToolResults.Should().Contain(item => item.ToolName == "edit_file_snapshot" && item.Succeeded);
            sessionId = result.Session.Id;
            (await File.ReadAllTextAsync(path)).Should().Be("after\r\n");
        }
        using var resumed = Services(new ScenarioProvider());
        var commands = resumed.GetRequiredService<IRuntimeCommandService>();
        var context = new RuntimeCommandContext(workspace, "model", PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build, SessionId: sessionId, IsInteractive: false);
        (await commands.UndoAsync(sessionId, context, CancellationToken.None)).Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("before\r\n");
        (await commands.RedoAsync(sessionId, context, CancellationToken.None)).Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be("after\r\n");
    }

    /// <summary>Parallel caller flows cannot see or cancel another caller's live turn.</summary>
    [Fact]
    public async Task Live_progress_and_cancellation_are_isolated_between_async_flows()
    {
        Directory.CreateDirectory(workspace);
        using var services = Services(new ScenarioProvider());
        var control = services.GetRequiredService<ILiveTurnControlService>();
        var publisher = services.GetRequiredService<IRuntimeEventPublisher>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        async Task RunAsync(string id)
        {
            using var handle = control.Begin();
            if (Interlocked.Increment(ref count) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await publisher.PublishAsync(new TurnSteeredEvent(id, id, "turn", DateTimeOffset.UtcNow, id));
            await using var reader = handle.ReadEventsAsync().GetAsyncEnumerator();
            (await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
            reader.Current.SessionId.Should().Be(id);
            handle.CancellationToken.IsCancellationRequested.Should().BeFalse();
            if (id == "first") { handle.Cancel(); firstCancelled.TrySetResult(); }
            else
            {
                // Check again after the other flow cancels, so a cancel-all regression cannot pass by ordering.
                await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
                handle.CancellationToken.IsCancellationRequested.Should().BeFalse();
            }
        }
        await Task.WhenAll(Task.Run(() => RunAsync("first")), Task.Run(() => RunAsync("second")));
    }

    /// <summary>Compacts a running tool loop and reuses its durable state from a fresh host.</summary>
    [Fact]
    public async Task Mid_turn_compaction_replays_after_host_restart_without_losing_source_events()
    {
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "large.txt"), new string('x', 30_000));
        var provider = new ScenarioProvider { ToolCalls = 6 };
        string sessionId;
        using (var services = Services(provider))
        {
            var result = await services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Keep API stable; inspect the file."), CancellationToken.None);
            sessionId = result.Session.Id;
            result.FinalOutput.Should().Be("Completed.");
            var events = await services.GetRequiredService<IEventStore>().ReadAllAsync(workspace, sessionId, CancellationToken.None);
            events.OfType<ToolCompletedEvent>().Count(item => item.Result.ToolName == "read_file").Should().Be(6);
            var compacted = events.OfType<ContextCompactedEvent>().ToArray();
            compacted.Should().NotBeEmpty();
            compacted.SelectMany(item => item.ArchivedMessages).SelectMany(item => item.Content).Should().Contain(block => block.Kind == ContentBlockKind.ToolResult && block.Text!.Contains(new string('x', 100)));
            result.Usage!.InputTokens.Should().BeGreaterThan(10, "summary usage is included");
            foreach (var request in provider.Requests.Where(item => !item.Id.StartsWith("compaction-"))) AssertToolPairs(request.Messages!);
        }
        var resumedProvider = new ScenarioProvider();
        using var resumed = Services(resumedProvider);
        await resumed.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Continue with verification.") with { SessionId = sessionId }, CancellationToken.None);
        var resumedRequest = resumedProvider.Requests.First(item => !item.Id.StartsWith("compaction-"));
        resumedRequest.Messages!.SelectMany(item => item.Content).Should().Contain(block => block.Text != null && block.Text.Contains("Keep API stable"));
        resumedRequest.Messages![0].Content[0].Text.Should().StartWith("Working state from earlier conversation");
    }

    /// <summary>A correction arriving during a final stream causes another request and survives replay.</summary>
    [Fact]
    public async Task Live_steering_is_consumed_after_stream_and_persisted_for_resume()
    {
        Directory.CreateDirectory(workspace);
        var provider = new ScenarioProvider { BlockFirstResponse = true };
        using var services = Services(provider);
        var control = services.GetRequiredService<ILiveTurnControlService>();
        using var handle = control.Begin();
        var runtime = services.GetRequiredService<IConversationRuntime>();
        var execution = runtime.RunPromptAsync(Request("Investigate."), handle.CancellationToken);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        handle.TrySteer("Keep the public API unchanged.").Should().BeTrue();
        provider.Release.TrySetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(15));
        var requests = provider.Requests.Where(item => !item.Id.StartsWith("compaction-")).ToArray();
        requests.Should().HaveCount(2);
        result.Usage!.TotalTokens.Should().Be(30, "both the original response and the correction consume tokens");
        requests[1].Messages!.SelectMany(item => item.Content).Should().Contain(block => block.Text == "Keep the public API unchanged.");
        var events = await services.GetRequiredService<IEventStore>().ReadAllAsync(workspace, result.Session.Id, CancellationToken.None);
        events.OfType<TurnSteeredEvent>().Should().ContainSingle().Which.Message.Should().Be("Keep the public API unchanged.");
        handle.GetPendingSteering().Should().BeEmpty();
        await runtime.RunPromptAsync(Request("Continue.") with { SessionId = result.Session.Id }, handle.CancellationToken);
        provider.Requests.Last().Messages!.SelectMany(item => item.Content).Should().Contain(block => block.Text == "Keep the public API unchanged.");
    }

    /// <summary>Cancellation settles the current turn and the same session remains executable.</summary>
    [Fact]
    public async Task Cancelled_turn_can_be_followed_by_another_turn()
    {
        Directory.CreateDirectory(workspace);
        var provider = new ScenarioProvider { BlockFirstResponse = true };
        using var services = Services(provider);
        var runtime = services.GetRequiredService<IConversationRuntime>();
        using (var handle = services.GetRequiredService<ILiveTurnControlService>().Begin())
        {
            handle.TrySteer("Keep accepted corrections after cancellation.").Should().BeTrue();
            var task = runtime.RunPromptAsync(Request("Wait."), handle.CancellationToken);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
            handle.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        var session = await services.GetRequiredService<ISessionStore>().GetLatestAsync(workspace, CancellationToken.None);
        session.Should().NotBeNull();
        var result = await runtime.RunPromptAsync(Request("Continue.") with { SessionId = session!.Id }, CancellationToken.None);
        result.FinalOutput.Should().Be("Completed.");
        result.Turn.SequenceNumber.Should().Be(2);
        provider.Requests.Last().Messages!.SelectMany(message => message.Content).Should().Contain(block => block.Text == "Keep accepted corrections after cancellation.");
    }

    /// <summary>Corrections at the final iteration stay queued instead of being acknowledged and dropped.</summary>
    [Fact]
    public async Task Steering_at_iteration_limit_remains_available_as_follow_up()
    {
        Directory.CreateDirectory(workspace);
        var provider = new ScenarioProvider { BlockFirstResponse = true };
        using var services = Services(provider, maxIterations: 1);
        using var handle = services.GetRequiredService<ILiveTurnControlService>().Begin();
        var execution = services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Investigate."), handle.CancellationToken);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(15));
        handle.TrySteer("Keep the API unchanged.").Should().BeTrue();
        provider.Release.TrySetResult();
        var result = await execution.WaitAsync(TimeSpan.FromSeconds(15));
        result.FinalOutput.Should().Be("Completed.");
        handle.GetPendingSteering().Should().Equal("Keep the API unchanged.");
        var events = await services.GetRequiredService<IEventStore>().ReadAllAsync(workspace, result.Session.Id, CancellationToken.None);
        events.OfType<TurnSteeredEvent>().Should().BeEmpty();
    }

    /// <summary>Later messages cannot rewrite the input captured for a prior provider invocation.</summary>
    [Fact]
    public async Task Provider_requests_remain_unchanged_when_context_management_is_not_registered()
    {
        Directory.CreateDirectory(workspace);
        var provider = new ScenarioProvider();
        using var services = Services(provider, withoutContextManager: true);
        var result = await services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Investigate."), CancellationToken.None);
        result.FinalOutput.Should().Be("Completed.");
        provider.Requests.Should().ContainSingle();
        provider.Requests.Single().Messages.Should().ContainSingle().Which.Role.Should().Be("user");
    }

    /// <summary>Overflow recovery retries only the buffered request and never dispatches partial failed tool calls.</summary>
    [Fact]
    public async Task Context_overflow_compacts_once_and_does_not_replay_tools()
    {
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "large.txt"), new string('x', 30_000));
        var provider = new ScenarioProvider { ToolCalls = 4, OverflowOnRequest = 3 };
        using var services = Services(provider);
        var result = await services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Inspect files."), CancellationToken.None);
        result.FinalOutput.Should().Be("Completed.");
        result.ToolResults.Should().HaveCount(3).And.OnlyContain(item => item.ToolName == "read_file");
        provider.Requests.Where(item => !item.Id.StartsWith("compaction-")).Should().HaveCount(5);
        var events = await services.GetRequiredService<IEventStore>().ReadAllAsync(workspace, result.Session.Id, CancellationToken.None);
        events.OfType<ContextCompactedEvent>().Should().ContainSingle();
    }

    /// <summary>Repeated provider rejection terminates without an unbounded maintenance/retry loop.</summary>
    [Fact]
    public async Task Repeated_context_overflow_stops_after_one_recovery()
    {
        Directory.CreateDirectory(workspace);
        await File.WriteAllTextAsync(Path.Combine(workspace, "large.txt"), new string('x', 30_000));
        var provider = new ScenarioProvider { ToolCalls = 4, OverflowOnRequest = 3, RepeatOverflow = true };
        using var services = Services(provider);
        var action = () => services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Inspect files."), CancellationToken.None);
        (await action.Should().ThrowAsync<ProviderExecutionException>()).Which.Kind.Should().Be(ProviderFailureKind.ContextWindowExceeded);
        provider.Requests.Where(item => !item.Id.StartsWith("compaction-")).Should().HaveCount(4);
        provider.Requests.Where(item => item.Id.StartsWith("compaction-")).Should().ContainSingle();
    }

    /// <summary>Only provider context is shortened; tool result contracts and durable events keep complete text.</summary>
    [Fact]
    public async Task Large_tool_output_is_bounded_without_changing_results_or_durable_events()
    {
        Directory.CreateDirectory(workspace);
        var full = new string('x', 100_000);
        await File.WriteAllTextAsync(Path.Combine(workspace, "large.txt"), full);
        var provider = new ScenarioProvider { ToolCalls = 1 };
        using var services = Services(provider);
        var result = await services.GetRequiredService<IConversationRuntime>().RunPromptAsync(Request("Inspect files."), CancellationToken.None);
        result.ToolResults.Should().ContainSingle().Which.Output.Should().Be("1|" + full);
        var preview = provider.Requests.Last().Messages!.SelectMany(message => message.Content).Single(block => block.Kind == ContentBlockKind.ToolResult).Text!;
        preview.Length.Should().BeLessThanOrEqualTo(32_000);
        preview.Should().Contain("read_tool_output");
        var events = await services.GetRequiredService<IEventStore>().ReadAllAsync(workspace, result.Session.Id, CancellationToken.None);
        events.OfType<ToolCompletedEvent>().Should().ContainSingle().Which.Result.Output.Should().Be("1|" + full);
    }

    private ServiceProvider Services(ScenarioProvider provider, int maxIterations = 25, bool withoutContextManager = false)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        if (withoutContextManager) services.RemoveAll<IAgentContextManager>();
        services.Configure<AutomaticCompactionOptions>(options => { options.TriggerRatio = 0.3; options.RetainedMessageCount = 4; });
        services.Configure<AgentLoopOptions>(options => { options.MaxTokensPerRequest = 1024; options.MaxToolIterations = maxIterations; });
        services.AddSingleton<IProviderRequestPreflight, Preflight>();
        services.AddSingleton<IAuthFlowService, Auth>();
        services.AddSingleton<IModelProviderResolver>(new Resolver(provider));
        return services.BuildServiceProvider();
    }
    private RunPromptRequest Request(string prompt) => new(prompt, null, workspace, PermissionMode.ReadOnly, OutputFormat.Text,
        new Dictionary<string, string> { ["provider"] = "milestone", ["model"] = "model" }, IsInteractive: false);

    private static void AssertToolPairs(IReadOnlyList<ChatMessage> messages)
    {
        var calls = messages.SelectMany(item => item.Content).Where(item => item.Kind == ContentBlockKind.ToolUse).Select(item => item.ToolUseId).ToArray();
        var results = messages.SelectMany(item => item.Content).Where(item => item.Kind == ContentBlockKind.ToolResult).Select(item => item.ToolUseId).ToArray();
        results.Should().BeEquivalentTo(calls);
    }
    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
    private sealed class Preflight : IProviderRequestPreflight { public ProviderRequest Prepare(ProviderRequest request) => request; }
    private sealed class Auth : IAuthFlowService { public Task<AuthStatus> GetStatusAsync(string name, CancellationToken token) => Task.FromResult(new AuthStatus("test", true, name, null, null, ["api"])); }
    private sealed class Resolver(IModelProvider provider) : IModelProviderResolver { public IModelProvider Resolve(string name) => provider; }

    private sealed class ScenarioProvider : IModelProvider
    {
        private int requests;
        public int ToolCalls { get; init; }
        public int OverflowOnRequest { get; init; }
        public bool RepeatOverflow { get; init; }
        public bool BlockFirstResponse { get; init; }
        public bool SnapshotEdit { get; init; }
        public ConcurrentQueue<ProviderRequest> Requests { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProviderName => "milestone";
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken token) => new Auth().GetStatusAsync(ProviderName, token);
        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken token)
        {
            Requests.Enqueue(request);
            return Task.FromResult(new ProviderStreamHandle(request, Stream(request, token)));
        }
        private async IAsyncEnumerable<ProviderEvent> Stream(ProviderRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            var summary = request.Id.StartsWith("compaction-", StringComparison.Ordinal);
            var index = summary ? 0 : Interlocked.Increment(ref requests);
            if (index == 1 && BlockFirstResponse)
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            if (!summary && OverflowOnRequest > 0 && (index == OverflowOnRequest || RepeatOverflow && index >= OverflowOnRequest))
            {
                yield return new("partial-tool", request.Id, "tool_use", DateTimeOffset.UtcNow, null, false, null, "tool_use", "never-execute", "write_file", "{}" );
                yield return new("overflow", request.Id, "failed", DateTimeOffset.UtcNow, "context_length_exceeded: maximum context length", true, null);
                yield break;
            }
            if (summary)
            {
                var state = new CompactionWorkingState("Inspect file", ["Keep API stable"], [], [], [], ["Finish inspection"]);
                yield return new("summary", request.Id, "delta", DateTimeOffset.UtcNow, JsonSerializer.Serialize(state, ProtocolJsonContext.Default.CompactionWorkingState), false, null);
            }
            else if (SnapshotEdit && index == 1)
                yield return new("read", request.Id, "tool_use", DateTimeOffset.UtcNow, null, false, null, "tool_use", "read-snapshot", "read_file_snapshot", """{"path":"editable.txt"}""");
            else if (SnapshotEdit && index == 2)
            {
                var text = request.Messages!.SelectMany(item => item.Content).Last(item => item.Kind == ContentBlockKind.ToolResult).Text!;
                var snapshotId = text.Split('\n')[0]["Snapshot: ".Length..];
                var args = JsonSerializer.Serialize(new { path = "editable.txt", snapshotId, edits = new[] { new { startLine = 1, deleteLineCount = 1, newText = "after\r\n" } } });
                yield return new("edit", request.Id, "tool_use", DateTimeOffset.UtcNow, null, false, null, "tool_use", "edit-snapshot", "edit_file_snapshot", args);
            }
            else if (index <= ToolCalls)
                yield return new($"tool-{index}", request.Id, "tool_use", DateTimeOffset.UtcNow, null, false, null, "tool_use", $"call-{index}", "read_file", """{"path":"large.txt"}""");
            else yield return new("answer", request.Id, "delta", DateTimeOffset.UtcNow, "Completed.", false, null);
            await Task.Yield();
            yield return new("done", request.Id, "done", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(summary ? 100 : 10, 5, 0, summary ? 105 : 15, null));
        }
    }
}
