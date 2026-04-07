using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies agent-driven runtime orchestration.
/// </summary>
public sealed class AgentRuntimeFlowTests
{
    /// <summary>
    /// Ensures prompt execution flows through the primary coding agent and emits agent lifecycle events.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_use_primary_agent_and_emit_agent_events()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        using var serviceProvider = services.BuildServiceProvider();

        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "explain the workspace briefly",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["model"] = "default"
                }),
            CancellationToken.None);

        result.Turn.AgentId.Should().Be("primary-coding-agent");
        result.FinalOutput.Should().NotBeNullOrWhiteSpace();
        result.Events.OfType<AgentSpawnedEvent>().Should().ContainSingle(spawned => spawned.AgentKind == "primaryCoding");
        result.Events.OfType<AgentCompletedEvent>().Should().ContainSingle(completed => completed.AgentId == "primary-coding-agent");
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-agent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }
}
