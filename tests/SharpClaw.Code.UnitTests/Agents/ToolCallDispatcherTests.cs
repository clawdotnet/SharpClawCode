using FluentAssertions;
using SharpClaw.Code.Agents.Internal;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.UnitTests.Agents;

/// <summary>
/// Verifies that <see cref="ToolCallDispatcher"/> bridges provider tool-use events to the tool executor
/// and produces correct content blocks and runtime events.
/// </summary>
public sealed class ToolCallDispatcherTests
{
    /// <summary>
    /// Ensures a successful tool execution returns a non-error tool-result content block
    /// with the tool output and the correct ToolUseId.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_executes_tool_and_returns_result_block()
    {
        var result = new ToolResult("req-1", "read_file", true, OutputFormat.Text, "file contents", null, 0, 100, null);
        var envelope = BuildEnvelope("read_file", result);
        var executor = new StubToolExecutor { ReturnValue = envelope };
        var publisher = new StubEventPublisher();
        var dispatcher = new ToolCallDispatcher(executor, publisher);

        var providerEvent = BuildToolUseEvent("tool-use-id-1", "read_file", "{}");
        var context = BuildContext();

        var (resultBlock, toolResult, events) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        resultBlock.Kind.Should().Be(ContentBlockKind.ToolResult);
        resultBlock.ToolUseId.Should().Be("tool-use-id-1");
        resultBlock.Text.Should().Be("file contents");
        resultBlock.IsError.Should().BeNull();
        toolResult.Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Ensures a failed tool execution returns an error-flagged tool-result content block
    /// with the error message.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_returns_error_block_on_tool_failure()
    {
        var result = new ToolResult("req-2", "write_file", false, OutputFormat.Text, null, "Permission denied", null, null, null);
        var envelope = BuildEnvelope("write_file", result);
        var executor = new StubToolExecutor { ReturnValue = envelope };
        var publisher = new StubEventPublisher();
        var dispatcher = new ToolCallDispatcher(executor, publisher);

        var providerEvent = BuildToolUseEvent("tool-use-id-2", "write_file", "{\"path\":\"x\"}");
        var context = BuildContext();

        var (resultBlock, toolResult, events) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        resultBlock.Kind.Should().Be(ContentBlockKind.ToolResult);
        resultBlock.ToolUseId.Should().Be("tool-use-id-2");
        resultBlock.Text.Should().Be("Permission denied");
        resultBlock.IsError.Should().Be(true);
        toolResult.Succeeded.Should().BeFalse();
    }

    /// <summary>
    /// Ensures a <see cref="ToolStartedEvent"/> and a <see cref="ToolCompletedEvent"/> are published
    /// in the correct order with matching session and turn identifiers.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_publishes_started_and_completed_events()
    {
        var result = new ToolResult("req-3", "bash", true, OutputFormat.Text, "done", null, 0, 50, null);
        var envelope = BuildEnvelope("bash", result);
        var executor = new StubToolExecutor { ReturnValue = envelope };
        var publisher = new StubEventPublisher();
        var dispatcher = new ToolCallDispatcher(executor, publisher);

        var providerEvent = BuildToolUseEvent("tool-use-id-3", "bash", "{\"command\":\"ls\"}");
        var context = BuildContext();

        var (_, _, events) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        publisher.Published.Should().HaveCount(2);
        events.Should().HaveCount(2);

        var startedEvent = publisher.Published[0].Should().BeOfType<ToolStartedEvent>().Subject;
        startedEvent.SessionId.Should().Be("s1");
        startedEvent.TurnId.Should().Be("t1");
        startedEvent.Request.ToolName.Should().Be("bash");

        var completedEvent = publisher.Published[1].Should().BeOfType<ToolCompletedEvent>().Subject;
        completedEvent.SessionId.Should().Be("s1");
        completedEvent.TurnId.Should().Be("t1");
        completedEvent.Result.Succeeded.Should().BeTrue();
    }

    private static ProviderEvent BuildToolUseEvent(string toolUseId, string toolName, string toolInputJson)
        => new(
            Id: "pev-1",
            RequestId: "req-1",
            Kind: "tool_use",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Content: null,
            IsTerminal: false,
            Usage: null,
            BlockType: "tool_use",
            ToolUseId: toolUseId,
            ToolName: toolName,
            ToolInputJson: toolInputJson);

    private static ToolExecutionContext BuildContext()
        => new(
            SessionId: "s1",
            TurnId: "t1",
            WorkspaceRoot: "/tmp/test",
            WorkingDirectory: "/tmp/test",
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            EnvironmentVariables: null);

    private static ToolExecutionEnvelope BuildEnvelope(string toolName, ToolResult result)
    {
        var request = new ToolExecutionRequest(
            Id: "req-1",
            SessionId: "s1",
            TurnId: "t1",
            ToolName: toolName,
            ArgumentsJson: "{}",
            ApprovalScope: ApprovalScope.ToolExecution,
            WorkingDirectory: "/tmp/test",
            RequiresApproval: false,
            IsDestructive: false);

        var decision = new PermissionDecision(
            Scope: ApprovalScope.ToolExecution,
            Mode: PermissionMode.WorkspaceWrite,
            IsAllowed: true,
            Reason: null,
            EvaluatedAtUtc: DateTimeOffset.UtcNow);

        return new ToolExecutionEnvelope(request, decision, result);
    }

    private sealed class StubToolExecutor : IToolExecutor
    {
        public ToolExecutionEnvelope? ReturnValue { get; set; }

        public Task<ToolExecutionEnvelope> ExecuteAsync(
            string toolName,
            string argumentsJson,
            ToolExecutionContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(ReturnValue!);
    }

    private sealed class StubEventPublisher : IRuntimeEventPublisher
    {
        public List<RuntimeEvent> Published { get; } = [];

        public ValueTask PublishAsync(
            RuntimeEvent runtimeEvent,
            RuntimeEventPublishOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Published.Add(runtimeEvent);
            return ValueTask.CompletedTask;
        }

        public IReadOnlyList<RuntimeEvent> GetRecentEventsSnapshot() => Published;
    }
}
