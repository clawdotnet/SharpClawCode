using SharpClaw.Code.Git.Models;

namespace SharpClaw.Code.Git.Abstractions;

/// <summary>
/// Provides structured inspection over a git-backed workspace.
/// </summary>
public interface IGitWorkspaceService
{
    /// <summary>
    /// Builds a git workspace snapshot for the supplied directory.
    /// </summary>
    /// <param name="workingDirectory">The directory to inspect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The git workspace snapshot.</returns>
    Task<GitWorkspaceSnapshot> GetSnapshotAsync(string workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the repository worktrees associated with the supplied directory.
    /// </summary>
    Task<GitWorktreeList> ListWorktreesAsync(string workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new repository worktree and optionally creates the backing branch.
    /// </summary>
    Task<GitWorktreeCreateResult> CreateWorktreeAsync(
        string workingDirectory,
        string path,
        string branchName,
        string? startPoint,
        bool useExistingBranch,
        CancellationToken cancellationToken);

    /// <summary>
    /// Prunes stale worktree administrative state from the repository.
    /// </summary>
    Task<GitWorktreePruneResult> PruneWorktreesAsync(string workingDirectory, CancellationToken cancellationToken);
}
