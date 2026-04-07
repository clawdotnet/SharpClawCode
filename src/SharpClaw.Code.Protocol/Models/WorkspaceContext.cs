namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents the current workspace and repository context for execution.
/// </summary>
/// <param name="RootPath">The logical workspace root path.</param>
/// <param name="CurrentPath">The current working directory within the workspace.</param>
/// <param name="IsGitRepository">Indicates whether the workspace is backed by Git.</param>
/// <param name="BranchName">The active branch name, if available.</param>
/// <param name="CommitSha">The current commit SHA, if available.</param>
/// <param name="Platform">The host operating system or platform label.</param>
/// <param name="AdditionalRoots">Additional directories included in the execution context.</param>
public sealed record WorkspaceContext(
    string RootPath,
    string CurrentPath,
    bool IsGitRepository,
    string? BranchName,
    string? CommitSha,
    string Platform,
    string[]? AdditionalRoots);
