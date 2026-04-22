using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies deep plan-mode prompt execution and todo synchronization.
/// </summary>
public sealed class PlanModeWorkflowTests
{
    /// <summary>
    /// Ensures plan mode returns structured output and synchronizes planning-owned session todos.
    /// </summary>
    [Fact]
    public async Task RunPrompt_plan_mode_should_create_structured_plan_and_sync_session_todos()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
            services.AddSingleton<IModelProviderResolver, PlanModelProviderResolver>();
        });
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var todoService = serviceProvider.GetRequiredService<ITodoService>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "Plan the next implementation slice for worktree automation",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "plan-provider",
                    ["model"] = "plan-model"
                },
                PrimaryMode: PrimaryMode.Plan),
            CancellationToken.None);

        result.PlanResult.Should().NotBeNull();
        result.FinalOutput.Should().Contain("Deep plan ready.");
        result.PlanResult!.Tasks.Should().HaveCount(2);
        result.Session.Metadata.Should().ContainKey(SharpClawWorkflowMetadataKeys.DeepPlanningSummary);

        var todos = await todoService.GetSnapshotAsync(workspacePath, result.Session.Id, CancellationToken.None);
        todos.SessionTodos.Should().ContainSingle(item => item.Title == "PLAN-001: Audit the current git/worktree seams" && item.OwnerAgentId == "deep-planning");
        todos.SessionTodos.Should().ContainSingle(item => item.Title == "PLAN-002: Wire worktree creation into session orchestration" && item.Status == TodoStatus.InProgress);
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-plan-mode-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private static ServiceProvider CreateRuntimeServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        configure(services);
        return services.BuildServiceProvider();
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("plan-subject", true, providerName, null, null, ["plan"]));
    }

    private sealed class PlanModelProviderResolver : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => new PlanModelProvider();
    }

    private sealed class PlanModelProvider : IModelProvider
    {
        public string ProviderName => "plan-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("plan-subject", true, ProviderName, null, null, ["plan"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request)));

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(ProviderRequest request)
        {
            yield return new ProviderEvent(
                "plan-event-1",
                request.Id,
                "delta",
                DateTimeOffset.Parse("2026-04-21T00:00:00Z"),
                """
                {
                  "summary": "Capture the current worktree behavior, then wire creation paths into the session flow incrementally.",
                  "assumptions": [
                    "Git is available in the target workspace."
                  ],
                  "risks": [
                    "Worktree creation can collide with existing paths or branch names."
                  ],
                  "nextAction": "Audit the existing git service and session orchestration seams before editing.",
                  "tasks": [
                    {
                      "id": "PLAN-001",
                      "title": "Audit the current git/worktree seams",
                      "status": "open",
                      "details": "Trace how git context is assembled and where worktree commands belong.",
                      "doneCriteria": "A clear integration path is identified."
                    },
                    {
                      "id": "PLAN-002",
                      "title": "Wire worktree creation into session orchestration",
                      "status": "inProgress",
                      "details": "Add the command/runtime path that can create isolated worktrees for future session flows.",
                      "doneCriteria": "A user-facing command can create and inspect worktrees."
                    }
                  ]
                }
                """,
                false,
                null);
            await Task.Yield();
            yield return new ProviderEvent(
                "plan-event-2",
                request.Id,
                "completed",
                DateTimeOffset.Parse("2026-04-21T00:00:01Z"),
                null,
                true,
                new UsageSnapshot(12, 24, 0, 36, null));
        }
    }
}
