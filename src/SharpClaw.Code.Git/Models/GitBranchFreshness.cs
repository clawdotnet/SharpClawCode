namespace SharpClaw.Code.Git.Models;

/// <summary>
/// Describes how the current branch compares to its upstream.
/// </summary>
/// <param name="HasUpstream">Indicates whether an upstream branch is configured.</param>
/// <param name="BehindBy">The number of commits behind the upstream branch.</param>
/// <param name="AheadBy">The number of commits ahead of the upstream branch.</param>
public sealed record GitBranchFreshness(
    bool HasUpstream,
    int BehindBy,
    int AheadBy);
