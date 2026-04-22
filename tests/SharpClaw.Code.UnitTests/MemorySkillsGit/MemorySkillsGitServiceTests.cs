using FluentAssertions;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Git.Models;
using SharpClaw.Code.Git.Services;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Memory.Services;
using SharpClaw.Code.Skills.Abstractions;
using SharpClaw.Code.Skills.Models;
using SharpClaw.Code.Skills.Services;

namespace SharpClaw.Code.UnitTests.MemorySkillsGit;

/// <summary>
/// Verifies the first practical memory, skills, and git services.
/// </summary>
public sealed class MemorySkillsGitServiceTests
{
    /// <summary>
    /// Ensures project memory loads from the local SharpClaw directory along with repo settings.
    /// </summary>
    [Fact]
    public async Task ProjectMemoryService_should_load_memory_and_settings_from_sharpclaw_directory()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var sharpClawPath = Path.Combine(workspacePath, ".sharpclaw");
        Directory.CreateDirectory(sharpClawPath);
        await File.WriteAllTextAsync(Path.Combine(sharpClawPath, "SHARPCLAW.md"), "# Repo memory\nPrefer small services.");
        await File.WriteAllTextAsync(Path.Combine(sharpClawPath, "settings.json"), """
            {
              "defaultModel": "sonnet",
              "reviewMode": "strict"
            }
            """);

        IProjectMemoryService service = new ProjectMemoryService(new LocalFileSystem(), new PathService(), new FixedClock());

        var context = await service.BuildContextAsync(workspacePath, CancellationToken.None);

        context.Memory.Should().NotBeNull();
        context.Memory!.Content.Should().Contain("Prefer small services.");
        context.RepositorySettings.Should().ContainKey("defaultModel");
        context.RepositorySettings["reviewMode"].Should().Be("strict");
        context.RenderForPrompt().Should().Contain("Project memory");
    }

    /// <summary>
    /// Ensures the local skill registry can install, list, and resolve skills.
    /// </summary>
    [Fact]
    public async Task SkillRegistry_should_install_list_and_resolve_local_skills()
    {
        var workspacePath = CreateTemporaryWorkspace();
        ISkillRegistry registry = new SkillRegistry(new LocalFileSystem(), new PathService(), new FixedClock());

        var installed = await registry.InstallAsync(
            workspacePath,
            new SkillInstallRequest(
                Id: "repo-review",
                Name: "Repo Review",
                Description: "Review the current repository for risks.",
                PromptTemplate: "Review {{target}} for correctness.",
                Version: "1.0.0",
                Tags: ["review", "quality"],
                Metadata: new Dictionary<string, string>
                {
                    ["executionRoute"] = "tool-agent-layer"
                }),
            CancellationToken.None);

        var listed = await registry.ListAsync(workspacePath, CancellationToken.None);
        var resolved = await registry.ResolveAsync(workspacePath, "repo-review", CancellationToken.None);

        installed.Definition.Name.Should().Be("Repo Review");
        listed.Should().ContainSingle(skill => skill.Id == "repo-review");
        resolved.Should().NotBeNull();
        resolved!.PromptTemplate.Should().Be("Review {{target}} for correctness.");
        resolved.Metadata["executionRoute"].Should().Be("tool-agent-layer");
    }

    /// <summary>
    /// Ensures git workspace status and branch freshness are parsed from git command output.
    /// </summary>
    [Fact]
    public async Task GitWorkspaceService_should_parse_status_diff_and_branch_freshness()
    {
        IGitWorkspaceService service = new GitWorkspaceService(new StubGitProcessRunner());

        var snapshot = await service.GetSnapshotAsync("/repo", CancellationToken.None);

        snapshot.IsRepository.Should().BeTrue();
        snapshot.RepositoryRoot.Should().Be("/repo");
        snapshot.CurrentBranch.Should().Be("main");
        snapshot.HeadCommitSha.Should().Be("abc123");
        snapshot.BranchFreshness.AheadBy.Should().Be(2);
        snapshot.BranchFreshness.BehindBy.Should().Be(1);
        snapshot.StatusEntries.Should().ContainSingle(entry => entry.Path == "src/Changed.cs");
        snapshot.DiffSummary.Should().Contain("1 file changed");
        snapshot.WorktreeCount.Should().Be(2);
        snapshot.IsLinkedWorktree.Should().BeFalse();
        snapshot.RenderForPrompt().Should().Contain("Branch: main");
    }

    /// <summary>
    /// Ensures worktree listing, creation, and pruning flow through the git workspace service.
    /// </summary>
    [Fact]
    public async Task GitWorkspaceService_should_list_create_and_prune_worktrees()
    {
        var runner = new StubGitProcessRunner();
        IGitWorkspaceService service = new GitWorkspaceService(runner);

        var initial = await service.ListWorktreesAsync("/repo", CancellationToken.None);
        var created = await service.CreateWorktreeAsync("/repo", "../repo-new", "feature/new", "HEAD", useExistingBranch: false, CancellationToken.None);
        var pruned = await service.PruneWorktreesAsync("/repo", CancellationToken.None);

        initial.Worktrees.Should().HaveCount(2);
        created.Worktree.Path.Should().Be(Path.GetFullPath(Path.Combine("/repo", "../repo-new")));
        created.Worktree.Branch.Should().Be("feature/new");
        pruned.PrunedCount.Should().Be(1);
        pruned.Worktrees.Should().NotContain(entry => entry.IsPrunable);
    }

    /// <summary>
    /// Ensures linked worktrees resolve the main worktree path from git-common-dir output.
    /// </summary>
    [Fact]
    public async Task GitWorkspaceService_should_detect_linked_worktree_main_path()
    {
        IGitWorkspaceService service = new GitWorkspaceService(new LinkedWorktreeProcessRunner());

        var snapshot = await service.GetSnapshotAsync("/repo-linked", CancellationToken.None);

        snapshot.IsLinkedWorktree.Should().BeTrue();
        Path.GetFullPath(snapshot.MainWorktreePath!).Should().Be(Path.GetFullPath("/repo"));
        snapshot.WorktreeCount.Should().Be(2);
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-memory-skill-git-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-04-06T00:00:00Z");
    }

    private sealed class StubGitProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, WorktreeState> worktrees = new(StringComparer.Ordinal)
        {
            ["/repo"] = new("main", "abc123", IsPrunable: false),
            ["/repo-linked"] = new("feature/worktrees", "def456", IsPrunable: true),
        };

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            var output = request.Arguments switch
            {
                ["rev-parse", "--show-toplevel"] => "/repo\n",
                ["rev-parse", "--path-format=absolute", "--git-common-dir"] => "/repo/.git\n",
                ["branch", "--show-current"] => "main\n",
                ["rev-parse", "HEAD"] => "abc123\n",
                ["status", "--porcelain=v1", "--branch"] => "## main...origin/main\n M src/Changed.cs\n",
                ["diff", "--no-ext-diff", "--stat"] => " src/Changed.cs | 2 +-\n 1 file changed, 1 insertion(+), 1 deletion(-)\n",
                ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"] => "1\t2\n",
                ["worktree", "list", "--porcelain"] => RenderWorktreeList(),
                ["worktree", "add", "-b", _, _, ..] => CreateWorktree(request.Arguments),
                ["worktree", "prune"] => PruneWorktrees(),
                _ => throw new InvalidOperationException($"Unexpected git command: {string.Join(' ', request.Arguments)}")
            };

            return Task.FromResult(new ProcessRunResult(0, output, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }

        private string CreateWorktree(string[] arguments)
        {
            var branch = arguments[3];
            var path = arguments[4];
            worktrees[path] = new WorktreeState(branch, "fedcba", IsPrunable: false);
            return $"Preparing worktree (new branch '{branch}')\nHEAD is now at fedcba\n";
        }

        private string PruneWorktrees()
        {
            foreach (var prunable in worktrees.Where(static pair => pair.Value.IsPrunable).Select(static pair => pair.Key).ToArray())
            {
                worktrees.Remove(prunable);
            }

            return "Pruned stale worktrees.\n";
        }

        private string RenderWorktreeList()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var pair in worktrees.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append("worktree ").AppendLine(pair.Key);
                builder.Append("HEAD ").AppendLine(pair.Value.Head);
                builder.Append("branch refs/heads/").AppendLine(pair.Value.Branch);
                if (pair.Value.IsPrunable)
                {
                    builder.AppendLine("prunable missing path");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private sealed record WorktreeState(string Branch, string Head, bool IsPrunable);
    }

    private sealed class LinkedWorktreeProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            var output = request.Arguments switch
            {
                ["rev-parse", "--show-toplevel"] => "/repo-linked\n",
                ["rev-parse", "--path-format=absolute", "--git-common-dir"] => "/repo/.git/worktrees/repo-linked\n",
                ["branch", "--show-current"] => "feature/worktrees\n",
                ["rev-parse", "HEAD"] => "def456\n",
                ["status", "--porcelain=v1", "--branch"] => "## feature/worktrees\n",
                ["diff", "--no-ext-diff", "--stat"] => string.Empty,
                ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"] => "0\t0\n",
                ["worktree", "list", "--porcelain"] => """
                    worktree /repo
                    HEAD abc123
                    branch refs/heads/main

                    worktree /repo-linked
                    HEAD def456
                    branch refs/heads/feature/worktrees

                    """,
                _ => throw new InvalidOperationException($"Unexpected git command: {string.Join(' ', request.Arguments)}")
            };

            return Task.FromResult(new ProcessRunResult(0, output, string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }
}
