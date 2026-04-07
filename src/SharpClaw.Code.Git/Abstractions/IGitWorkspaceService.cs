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
}
