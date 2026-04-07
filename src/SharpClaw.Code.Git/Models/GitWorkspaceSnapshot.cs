namespace SharpClaw.Code.Git.Models;

/// <summary>
/// Represents a structured git snapshot for a workspace.
/// </summary>
/// <param name="IsRepository">Indicates whether the workspace is a git repository.</param>
/// <param name="RepositoryRoot">The repository root path, if available.</param>
/// <param name="CurrentBranch">The current branch name, if available.</param>
/// <param name="HeadCommitSha">The current HEAD commit SHA, if available.</param>
/// <param name="BranchFreshness">The branch freshness relative to upstream.</param>
/// <param name="StatusEntries">The parsed git status entries.</param>
/// <param name="StatusSummary">A concise status summary.</param>
/// <param name="DiffSummary">A concise diff summary.</param>
public sealed record GitWorkspaceSnapshot(
    bool IsRepository,
    string? RepositoryRoot,
    string? CurrentBranch,
    string? HeadCommitSha,
    GitBranchFreshness BranchFreshness,
    IReadOnlyList<GitStatusEntry> StatusEntries,
    string? StatusSummary,
    string? DiffSummary)
{
    /// <summary>
    /// Renders the git snapshot as a prompt-ready section.
    /// </summary>
    /// <returns>The prompt section text.</returns>
    public string RenderForPrompt()
    {
        if (!IsRepository)
        {
            return "Git context:\n- Not a git repository.";
        }

        var lines = new List<string>
        {
            "Git context:",
            $"- Branch: {CurrentBranch ?? "(unknown)"}",
            $"- HEAD: {HeadCommitSha ?? "(unknown)"}"
        };

        if (BranchFreshness.HasUpstream)
        {
            lines.Add($"- Upstream freshness: ahead {BranchFreshness.AheadBy}, behind {BranchFreshness.BehindBy}");
        }

        if (!string.IsNullOrWhiteSpace(StatusSummary))
        {
            lines.Add($"- Status: {StatusSummary}");
        }

        if (!string.IsNullOrWhiteSpace(DiffSummary))
        {
            lines.Add($"- Diff: {DiffSummary}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
