using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Git.Models;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Skills.Abstractions;
using SharpClaw.Code.Skills.Models;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies runtime prompt context assembly from project memory, skills, and git state.
/// </summary>
public sealed class PromptContextAssemblyTests
{
    /// <summary>
    /// Ensures the runtime includes memory, skill metadata, and git context in provider-backed prompt execution.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_include_memory_skills_and_git_context_in_provider_request()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IProjectMemoryService>(new StubProjectMemoryService());
        services.AddSingleton<ISessionSummaryService>(new StubSessionSummaryService());
        services.AddSingleton<ISkillRegistry>(new StubSkillRegistry());
        services.AddSingleton<IGitWorkspaceService>(new StubGitWorkspaceService());
        services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
        services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
        services.AddSingleton<IModelProviderResolver, StubModelProviderResolver>();
        using var serviceProvider = services.BuildServiceProvider();

        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "summarize the repo state",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "stub-provider",
                    ["model"] = "stub-model"
                }),
            CancellationToken.None);

        var providerStarted = result.Events.OfType<ProviderStartedEvent>().Single();
        providerStarted.Request.Prompt.Should().Contain("Project memory");
        providerStarted.Request.Prompt.Should().Contain("Prefer service seams.");
        providerStarted.Request.Prompt.Should().Contain("Available skills");
        providerStarted.Request.Prompt.Should().Contain("repo-review");
        providerStarted.Request.Prompt.Should().Contain("Git context");
        providerStarted.Request.Prompt.Should().Contain("Branch: main");
        providerStarted.Request.Prompt.Should().Contain("Session summary");
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private sealed class StubProjectMemoryService : IProjectMemoryService
    {
        public Task<ProjectMemoryContext> BuildContextAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new ProjectMemoryContext(
                new ProjectMemory("memory-001", "project", "Prefer service seams.", "test", DateTimeOffset.UtcNow, null, null),
                new Dictionary<string, string>
                {
                    ["defaultModel"] = "sonnet"
                }));
    }

    private sealed class StubSessionSummaryService : ISessionSummaryService
    {
        public Task<string?> BuildSummaryAsync(ConversationSession session, CancellationToken cancellationToken)
            => Task.FromResult<string?>("Most recent work focused on runtime orchestration.");
    }

    private sealed class StubSkillRegistry : ISkillRegistry
    {
        public Task<ResolvedSkill?> ResolveAsync(string workspaceRoot, string skillIdOrName, CancellationToken cancellationToken)
            => Task.FromResult<ResolvedSkill?>(null);

        public Task<IReadOnlyList<SkillDefinition>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<SkillDefinition>>([
                new SkillDefinition("repo-review", "Repo Review", "Review the repository.", workspaceRoot, "1.0.0", ["review"], "prompt.txt")
            ]);

        public Task<ResolvedSkill> InstallAsync(string workspaceRoot, SkillInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubGitWorkspaceService : IGitWorkspaceService
    {
        public Task<GitWorkspaceSnapshot> GetSnapshotAsync(string workingDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitWorkspaceSnapshot(
                IsRepository: true,
                RepositoryRoot: workingDirectory,
                CurrentBranch: "main",
                HeadCommitSha: "abc123",
                BranchFreshness: new GitBranchFreshness(true, 0, 0),
                StatusEntries: [],
                StatusSummary: "Clean working tree.",
                DiffSummary: "No pending diff."));
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("stub-subject", true, providerName, null, null, ["api"]));
    }

    private sealed class StubModelProviderResolver : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => new StubModelProvider();
    }

    private sealed class StubModelProvider : IModelProvider
    {
        public string ProviderName => "stub-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("stub-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request)));

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(ProviderRequest request)
        {
            yield return new ProviderEvent("provider-event-1", request.Id, "delta", DateTimeOffset.UtcNow, "Context applied.", false, null);
            await Task.Yield();
            yield return new ProviderEvent("provider-event-2", request.Id, "completed", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
        }
    }
}
