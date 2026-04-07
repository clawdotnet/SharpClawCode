using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Git.Models;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Git.Services;

/// <summary>
/// Inspects a git workspace using the local git CLI through testable process abstractions.
/// </summary>
public sealed class GitWorkspaceService(IProcessRunner processRunner) : IGitWorkspaceService
{
    /// <inheritdoc />
    public async Task<GitWorkspaceSnapshot> GetSnapshotAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var repositoryRootResult = await RunGitAsync(workingDirectory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (repositoryRootResult.ExitCode != 0)
        {
            return new GitWorkspaceSnapshot(
                IsRepository: false,
                RepositoryRoot: null,
                CurrentBranch: null,
                HeadCommitSha: null,
                BranchFreshness: new GitBranchFreshness(false, 0, 0),
                StatusEntries: [],
                StatusSummary: null,
                DiffSummary: null);
        }

        var repositoryRoot = NormalizeLine(repositoryRootResult.StandardOutput);
        var currentBranchTask = RunGitAsync(workingDirectory, ["branch", "--show-current"], cancellationToken);
        var headTask = RunGitAsync(workingDirectory, ["rev-parse", "HEAD"], cancellationToken);
        var statusTask = RunGitAsync(workingDirectory, ["status", "--porcelain=v1", "--branch"], cancellationToken);
        var diffTask = RunGitAsync(workingDirectory, ["diff", "--no-ext-diff", "--stat"], cancellationToken);
        var freshnessTask = RunGitAsync(workingDirectory, ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"], cancellationToken);

        await Task.WhenAll(currentBranchTask, headTask, statusTask, diffTask, freshnessTask).ConfigureAwait(false);

        var currentBranchResult = await currentBranchTask.ConfigureAwait(false);
        var headResult = await headTask.ConfigureAwait(false);
        var statusResult = await statusTask.ConfigureAwait(false);
        var diffResult = await diffTask.ConfigureAwait(false);
        var freshnessResult = await freshnessTask.ConfigureAwait(false);

        var statusEntries = ParseStatusEntries(statusResult.StandardOutput);
        return new GitWorkspaceSnapshot(
            IsRepository: true,
            RepositoryRoot: repositoryRoot,
            CurrentBranch: NormalizeLine(currentBranchResult.StandardOutput),
            HeadCommitSha: NormalizeLine(headResult.StandardOutput),
            BranchFreshness: ParseFreshness(freshnessResult),
            StatusEntries: statusEntries,
            StatusSummary: CreateStatusSummary(statusEntries),
            DiffSummary: NormalizeMultiline(diffResult.StandardOutput));
    }

    private Task<ProcessRunResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => processRunner.RunAsync(new ProcessRunRequest("git", arguments.ToArray(), workingDirectory, null), cancellationToken);

    private static IReadOnlyList<GitStatusEntry> ParseStatusEntries(string output)
    {
        var entries = new List<GitStatusEntry>();
        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("##", StringComparison.Ordinal) || line.Length < 4)
            {
                continue;
            }

            entries.Add(new GitStatusEntry(
                Path: line[3..].Trim(),
                IndexStatus: line[0].ToString(),
                WorkingTreeStatus: line[1].ToString()));
        }

        return entries;
    }

    private static GitBranchFreshness ParseFreshness(ProcessRunResult result)
    {
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return new GitBranchFreshness(false, 0, 0);
        }

        var parts = result.StandardOutput.Trim().Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
               && int.TryParse(parts[0], out var behindBy)
               && int.TryParse(parts[1], out var aheadBy)
            ? new GitBranchFreshness(true, behindBy, aheadBy)
            : new GitBranchFreshness(false, 0, 0);
    }

    private static string? NormalizeLine(string output)
    {
        var normalized = output.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeMultiline(string output)
    {
        var lines = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
        return lines.Length == 0 ? null : string.Join(" ", lines);
    }

    private static string CreateStatusSummary(IReadOnlyList<GitStatusEntry> statusEntries)
        => statusEntries.Count == 0
            ? "Clean working tree."
            : $"{statusEntries.Count} changed item(s): {string.Join(", ", statusEntries.Select(entry => entry.Path))}";
}
