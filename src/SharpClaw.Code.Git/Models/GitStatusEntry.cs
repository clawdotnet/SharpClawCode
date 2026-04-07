namespace SharpClaw.Code.Git.Models;

/// <summary>
/// Represents a single git status entry from porcelain output.
/// </summary>
/// <param name="Path">The affected file path.</param>
/// <param name="IndexStatus">The staged status code.</param>
/// <param name="WorkingTreeStatus">The working tree status code.</param>
public sealed record GitStatusEntry(
    string Path,
    string IndexStatus,
    string WorkingTreeStatus);
