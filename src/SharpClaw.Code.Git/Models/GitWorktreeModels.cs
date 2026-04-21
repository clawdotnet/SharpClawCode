namespace SharpClaw.Code.Git.Models;

/// <summary>
/// Describes a single git worktree entry.
/// </summary>
public sealed record GitWorktreeEntry(
    string Path,
    string? Branch,
    string? HeadCommitSha,
    bool IsCurrent,
    bool IsLocked,
    bool IsPrunable,
    bool IsDetached,
    bool IsBare,
    string? LockReason,
    string? PrunableReason);

/// <summary>
/// Lists worktrees associated with a repository.
/// </summary>
public sealed record GitWorktreeList(
    string RepositoryRoot,
    string MainWorktreePath,
    IReadOnlyList<GitWorktreeEntry> Worktrees);

/// <summary>
/// Describes the result of creating a git worktree.
/// </summary>
public sealed record GitWorktreeCreateResult(
    string RepositoryRoot,
    GitWorktreeEntry Worktree,
    bool CreatedBranch,
    string? StartPoint);

/// <summary>
/// Describes the result of pruning stale git worktree metadata.
/// </summary>
public sealed record GitWorktreePruneResult(
    string RepositoryRoot,
    int PrunedCount,
    string? Output,
    IReadOnlyList<GitWorktreeEntry> Worktrees);
