using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Reads git workspace metadata when available.
/// </summary>
public sealed class GitAvailabilityCheck(IGitWorkspaceService gitWorkspaceService) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "git.snapshot";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await gitWorkspaceService.GetSnapshotAsync(context.NormalizedWorkspacePath, cancellationToken).ConfigureAwait(false);
            if (!snapshot.IsRepository)
            {
                return new OperationalCheckItem(
                    Id,
                    OperationalCheckStatus.Warn,
                    "Directory is not a git repository.",
                    snapshot.StatusSummary);
            }

            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Ok,
                "Git snapshot available.",
                $"{snapshot.CurrentBranch} @ {snapshot.HeadCommitSha?[..Math.Min(7, snapshot.HeadCommitSha.Length)]} — {snapshot.StatusSummary}");
        }
        catch (Exception exception)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Warn,
                "Git inspection failed.",
                exception.Message);
        }
    }
}
