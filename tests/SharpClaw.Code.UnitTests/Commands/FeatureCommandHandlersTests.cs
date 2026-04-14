using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.UnitTests.Commands;

public sealed class FeatureCommandHandlersTests
{
    [Fact]
    public async Task Usage_command_should_render_workspace_usage_payload()
    {
        var renderer = new RecordingRenderer();
        var handler = new UsageCommandHandler(new StubInsightsService(), new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build, "session-1");

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "usage", []), context, CancellationToken.None);

        exitCode.Should().Be(0);
        renderer.LastResult.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize(renderer.LastResult!.DataJson!, ProtocolJsonContext.Default.WorkspaceUsageReport);
        payload!.WorkspaceTotal.TotalTokens.Should().Be(42);
    }

    [Fact]
    public async Task Hooks_command_should_execute_named_test_from_slash_command()
    {
        var renderer = new RecordingRenderer();
        var dispatcher = new StubHookDispatcher();
        var handler = new HooksCommandHandler(dispatcher, new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build);

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "hooks", ["test", "post-turn"]), context, CancellationToken.None);

        exitCode.Should().Be(0);
        dispatcher.LastTestName.Should().Be("post-turn");
        renderer.LastResult!.Succeeded.Should().BeTrue();
    }

    private sealed class RecordingRenderer : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Json;

        public CommandResult? LastResult { get; private set; }

        public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
        {
            LastResult = result;
            return Task.CompletedTask;
        }

        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubInsightsService : IWorkspaceInsightsService
    {
        public Task<WorkspaceUsageReport> BuildUsageReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceUsageReport(
                workspaceRoot,
                currentSessionId,
                currentSessionId,
                new UsageSnapshot(30, 12, 0, 42, 0.05m),
                [new SessionUsageReport("session-1", "Session", true, true, new UsageSnapshot(30, 12, 0, 42, 0.05m))]));

        public Task<WorkspaceCostReport> BuildCostReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WorkspaceStatsReport> BuildStatsReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubHookDispatcher : IHookDispatcher
    {
        public string? LastTestName { get; private set; }

        public Task DispatchAsync(string workspaceRoot, HookTriggerKind trigger, string payloadJson, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<HookStatusRecord>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<HookStatusRecord>>([
                new HookStatusRecord("post-turn", HookTriggerKind.TurnCompleted, "echo", ["ok"], true)
            ]);

        public Task<HookTestResult> TestAsync(string workspaceRoot, string hookName, string payloadJson, CancellationToken cancellationToken)
        {
            LastTestName = hookName;
            return Task.FromResult(new HookTestResult(hookName, HookTriggerKind.TurnCompleted, true, "Hook executed successfully.", DateTimeOffset.UtcNow));
        }
    }
}
