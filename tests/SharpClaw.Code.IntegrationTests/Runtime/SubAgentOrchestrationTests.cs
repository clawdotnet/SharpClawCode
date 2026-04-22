using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies bounded delegated subagent execution flows through the main runtime turn.
/// </summary>
public sealed class SubAgentOrchestrationTests
{
    /// <summary>
    /// Ensures the primary agent can delegate a bounded read-only investigation to the subagent worker.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_execute_subagent_tool_calls_and_emit_child_agent_events()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
        services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
        services.AddSingleton<IModelProviderResolver>(_ => new StaticModelProviderResolver(new SubAgentScenarioProvider()));
        using var serviceProvider = services.BuildServiceProvider();

        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "Investigate the auth flow with a helper agent.",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "subagent-provider",
                    ["model"] = "subagent-model"
                }),
            CancellationToken.None);

        result.FinalOutput.Should().Contain("Primary agent integrated subagent output.");
        result.ToolResults.Should().ContainSingle(tool => tool.ToolName == "use_subagents" && tool.Succeeded);
        result.Events.OfType<AgentSpawnedEvent>().Should().Contain(spawned =>
            spawned.AgentKind == "subAgent" &&
            spawned.ParentAgentId == "primary-coding-agent");
        result.Events.OfType<AgentCompletedEvent>().Should().Contain(completed =>
            completed.AgentId == "sub-agent-worker" &&
            completed.Succeeded);
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-subagent-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        File.WriteAllText(Path.Combine(workspacePath, "Program.cs"), "class Program {}");
        return workspacePath;
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("subagent-subject", true, providerName, null, null, ["api"]));
    }

    private sealed class StaticModelProviderResolver(IModelProvider provider) : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => provider;
    }

    private sealed class SubAgentScenarioProvider : IModelProvider
    {
        public string ProviderName => "subagent-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("subagent-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request, cancellationToken)));

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
            ProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (request.SystemPrompt?.Contains("bounded SharpClaw sub-agent", StringComparison.Ordinal) == true)
            {
                yield return new ProviderEvent("child-1", request.Id, "delta", DateTimeOffset.UtcNow, "Subagent investigation complete.", false, null);
                await Task.Yield();
                yield return new ProviderEvent("child-2", request.Id, "done", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
                yield break;
            }

            if (HasToolResultInMessages(request))
            {
                yield return new ProviderEvent("parent-2", request.Id, "delta", DateTimeOffset.UtcNow, "Primary agent integrated subagent output.", false, null);
                await Task.Yield();
                yield return new ProviderEvent("parent-3", request.Id, "done", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
                yield break;
            }

            yield return new ProviderEvent(
                "parent-1",
                request.Id,
                "tool_use",
                DateTimeOffset.UtcNow,
                null,
                false,
                null,
                BlockType: "tool_use",
                ToolUseId: "toolu_subagent_001",
                ToolName: "use_subagents",
                ToolInputJson: """{"tasks":[{"goal":"Inspect the auth entry point","expectedOutput":"Return concise findings","constraints":["Stay read-only"]}]}""");
            await Task.Yield();
            yield return new ProviderEvent("parent-1-done", request.Id, "done", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
        }

        private static bool HasToolResultInMessages(ProviderRequest request)
            => request.Messages is not null
               && request.Messages.SelectMany(static message => message.Content).Any(static block => block.Kind == ContentBlockKind.ToolResult);
    }
}
