using FluentAssertions;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Internal;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
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

        var dispatcher = new ToolCallDispatcher(executor, new StubSubAgentOrchestrator());

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

        var dispatcher = new ToolCallDispatcher(executor, new StubSubAgentOrchestrator());

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
    /// Ensures the dispatcher does not publish events itself (ToolExecutor handles event publishing)
    /// and returns an empty events list to avoid duplicates.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_returns_empty_events_to_avoid_duplicates()
    {
        var result = new ToolResult("req-3", "bash", true, OutputFormat.Text, "done", null, 0, 50, null);
        var envelope = BuildEnvelope("bash", result);
        var executor = new StubToolExecutor { ReturnValue = envelope };
        var dispatcher = new ToolCallDispatcher(executor, new StubSubAgentOrchestrator());

        var providerEvent = BuildToolUseEvent("tool-use-id-3", "bash", "{\"command\":\"ls\"}");
        var context = BuildContext();

        var (_, _, events) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        // ToolCallDispatcher delegates event publishing to ToolExecutor; returns empty list.
        events.Should().BeEmpty();
    }

    /// <summary>
    /// Ensures the dispatcher returns an error block when ToolName is missing.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_returns_error_when_tool_name_missing()
    {
        var executor = new StubToolExecutor();
        var dispatcher = new ToolCallDispatcher(executor, new StubSubAgentOrchestrator());

        var providerEvent = BuildToolUseEvent("tool-use-id-4", null!, "{}");
        var context = BuildContext();

        var (resultBlock, _, _) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        resultBlock.Kind.Should().Be(ContentBlockKind.ToolResult);
        resultBlock.IsError.Should().Be(true);
        resultBlock.Text.Should().Contain("tool name");
    }

    /// <summary>
    /// Ensures delegated subagent tool calls return the orchestrator payload and runtime events.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_handles_subagent_tool_calls()
    {
        var executor = new StubToolExecutor();
        RuntimeEvent[] subAgentEvents =
        [
            new AgentSpawnedEvent("event-1", "s1", "t1", DateTimeOffset.UtcNow, "sub-agent-worker", "subAgent", "primary-coding-agent")
        ];
        var orchestrator = new StubSubAgentOrchestrator
        {
            ReturnValue = new SubAgentBatchExecutionResult(
                new SubAgentBatchResult(
                    [
                        new SubAgentTaskResult("task-1", "Inspect auth flow", "Return concise findings", true, "Found the auth entry point.", null, "sub-agent-worker")
                    ],
                    CompletedCount: 1,
                    FailedCount: 0),
                subAgentEvents)
        };
        var dispatcher = new ToolCallDispatcher(executor, orchestrator);

        var providerEvent = BuildToolUseEvent(
            "tool-use-id-5",
            "use_subagents",
            """{"tasks":[{"goal":"Inspect auth flow","expectedOutput":"Return concise findings"}]}""");
        var context = BuildContext();

        var (resultBlock, toolResult, events) = await dispatcher.DispatchAsync(providerEvent, context, CancellationToken.None);

        resultBlock.IsError.Should().BeNull();
        resultBlock.Text.Should().Contain("Inspect auth flow");
        toolResult.ToolName.Should().Be("use_subagents");
        toolResult.Succeeded.Should().BeTrue();
        events.Should().ContainSingle(ev => ev is AgentSpawnedEvent);
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

    private sealed class StubSubAgentOrchestrator : ISubAgentOrchestrator
    {
        public SubAgentBatchExecutionResult? ReturnValue { get; set; }

        public Task<SubAgentBatchExecutionResult> ExecuteAsync(
            SubAgentBatchRequest request,
            ToolExecutionContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(ReturnValue ?? new SubAgentBatchExecutionResult(
                new SubAgentBatchResult([], 0, 0),
                []));
    }
}
