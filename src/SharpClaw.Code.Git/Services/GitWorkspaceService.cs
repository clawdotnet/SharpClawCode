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
                DiffSummary: null,
                IsLinkedWorktree: false,
                MainWorktreePath: null,
                WorktreeCount: 0);
        }

        var repositoryRoot = NormalizeLine(repositoryRootResult.StandardOutput);
        var currentBranchTask = RunGitAsync(workingDirectory, ["branch", "--show-current"], cancellationToken);
        var headTask = RunGitAsync(workingDirectory, ["rev-parse", "HEAD"], cancellationToken);
        var statusTask = RunGitAsync(workingDirectory, ["status", "--porcelain=v1", "--branch"], cancellationToken);
        var diffTask = RunGitAsync(workingDirectory, ["diff", "--no-ext-diff", "--stat"], cancellationToken);
        var freshnessTask = RunGitAsync(workingDirectory, ["rev-list", "--left-right", "--count", "@{upstream}...HEAD"], cancellationToken);
        var worktreeListTask = ListWorktreesCoreAsync(workingDirectory, repositoryRoot ?? workingDirectory, cancellationToken);

        await Task.WhenAll(currentBranchTask, headTask, statusTask, diffTask, freshnessTask, worktreeListTask).ConfigureAwait(false);

        var currentBranchResult = await currentBranchTask.ConfigureAwait(false);
        var headResult = await headTask.ConfigureAwait(false);
        var statusResult = await statusTask.ConfigureAwait(false);
        var diffResult = await diffTask.ConfigureAwait(false);
        var freshnessResult = await freshnessTask.ConfigureAwait(false);
        var worktreeList = await worktreeListTask.ConfigureAwait(false);

        var statusEntries = ParseStatusEntries(statusResult.StandardOutput);
        return new GitWorkspaceSnapshot(
            IsRepository: true,
            RepositoryRoot: repositoryRoot,
            CurrentBranch: NormalizeLine(currentBranchResult.StandardOutput),
            HeadCommitSha: NormalizeLine(headResult.StandardOutput),
            BranchFreshness: ParseFreshness(freshnessResult),
            StatusEntries: statusEntries,
            StatusSummary: CreateStatusSummary(statusEntries),
            DiffSummary: NormalizeMultiline(diffResult.StandardOutput),
            IsLinkedWorktree: repositoryRoot is not null && !PathsEqual(repositoryRoot, worktreeList.MainWorktreePath),
            MainWorktreePath: worktreeList.MainWorktreePath,
            WorktreeCount: worktreeList.Worktrees.Count);
    }

    /// <inheritdoc />
    public async Task<GitWorktreeList> ListWorktreesAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var repositoryRootResult = await RunGitAsync(workingDirectory, ["rev-parse", "--show-toplevel"], cancellationToken).ConfigureAwait(false);
        if (repositoryRootResult.ExitCode != 0)
        {
            throw new InvalidOperationException("The supplied directory is not a git repository.");
        }

        var repositoryRoot = NormalizeLine(repositoryRootResult.StandardOutput) ?? workingDirectory;
        return await ListWorktreesCoreAsync(workingDirectory, repositoryRoot, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<GitWorktreeCreateResult> CreateWorktreeAsync(
        string workingDirectory,
        string path,
        string branchName,
        string? startPoint,
        bool useExistingBranch,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        var worktreeList = await ListWorktreesAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        var resolvedPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workingDirectory, path));

        var arguments = BuildCreateArguments(resolvedPath, branchName.Trim(), startPoint, useExistingBranch);
        var result = await RunGitAsync(workingDirectory, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("Failed to create git worktree.", result));
        }

        var refreshed = await ListWorktreesCoreAsync(workingDirectory, worktreeList.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        var created = refreshed.Worktrees.FirstOrDefault(entry => PathsEqual(entry.Path, resolvedPath))
            ?? new GitWorktreeEntry(
                resolvedPath,
                branchName.Trim(),
                null,
                false,
                false,
                false,
                false,
                false,
                null,
                null);

        return new GitWorktreeCreateResult(
            refreshed.RepositoryRoot,
            created,
            CreatedBranch: !useExistingBranch,
            StartPoint: string.IsNullOrWhiteSpace(startPoint) ? null : startPoint.Trim());
    }

    /// <inheritdoc />
    public async Task<GitWorktreePruneResult> PruneWorktreesAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var before = await ListWorktreesAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
        var pruneResult = await RunGitAsync(workingDirectory, ["worktree", "prune"], cancellationToken).ConfigureAwait(false);
        if (pruneResult.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("Failed to prune git worktrees.", pruneResult));
        }

        var after = await ListWorktreesCoreAsync(workingDirectory, before.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        return new GitWorktreePruneResult(
            before.RepositoryRoot,
            Math.Max(before.Worktrees.Count - after.Worktrees.Count, 0),
            NormalizeMultiline(string.Join(Environment.NewLine, [pruneResult.StandardOutput, pruneResult.StandardError])),
            after.Worktrees);
    }

    private Task<ProcessRunResult> RunGitAsync(string workingDirectory, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => processRunner.RunAsync(new ProcessRunRequest("git", arguments.ToArray(), workingDirectory, null), cancellationToken);

    private async Task<GitWorktreeList> ListWorktreesCoreAsync(string workingDirectory, string repositoryRoot, CancellationToken cancellationToken)
    {
        var commonDirTask = RunGitAsync(workingDirectory, ["rev-parse", "--path-format=absolute", "--git-common-dir"], cancellationToken);
        var listTask = RunGitAsync(workingDirectory, ["worktree", "list", "--porcelain"], cancellationToken);

        await Task.WhenAll(commonDirTask, listTask).ConfigureAwait(false);

        var commonDirResult = await commonDirTask.ConfigureAwait(false);
        var listResult = await listTask.ConfigureAwait(false);
        if (listResult.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("Failed to list git worktrees.", listResult));
        }

        var mainWorktreePath = commonDirResult.ExitCode == 0
            ? ResolveMainWorktreePath(commonDirResult, repositoryRoot)
            : repositoryRoot;
        var worktrees = ParseWorktreeEntries(listResult.StandardOutput, repositoryRoot);
        if (worktrees.Count == 0)
        {
            worktrees =
            [
                new GitWorktreeEntry(
                    repositoryRoot,
                    null,
                    null,
                    true,
                    false,
                    false,
                    false,
                    false,
                    null,
                    null)
            ];
        }

        return new GitWorktreeList(repositoryRoot, mainWorktreePath, worktrees);
    }

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

    private static string[] BuildCreateArguments(string path, string branchName, string? startPoint, bool useExistingBranch)
    {
        if (useExistingBranch)
        {
            return ["worktree", "add", path, branchName];
        }

        return string.IsNullOrWhiteSpace(startPoint)
            ? ["worktree", "add", "-b", branchName, path]
            : ["worktree", "add", "-b", branchName, path, startPoint.Trim()];
    }

    private static List<GitWorktreeEntry> ParseWorktreeEntries(string output, string repositoryRoot)
    {
        var entries = new List<GitWorktreeEntry>();
        string? currentPath = null;
        string? currentHead = null;
        string? currentBranch = null;
        string? currentLockReason = null;
        string? currentPrunableReason = null;
        var currentDetached = false;
        var currentBare = false;
        var currentLocked = false;
        var currentPrunable = false;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(currentPath))
            {
                return;
            }

            var normalizedPath = currentPath.Trim();
            entries.Add(new GitWorktreeEntry(
                normalizedPath,
                NormalizeBranch(currentBranch),
                string.IsNullOrWhiteSpace(currentHead) ? null : currentHead.Trim(),
                IsCurrent: PathsEqual(normalizedPath, repositoryRoot),
                IsLocked: currentLocked,
                IsPrunable: currentPrunable,
                IsDetached: currentDetached,
                IsBare: currentBare,
                LockReason: currentLockReason,
                PrunableReason: currentPrunableReason));

            currentPath = null;
            currentHead = null;
            currentBranch = null;
            currentLockReason = null;
            currentPrunableReason = null;
            currentDetached = false;
            currentBare = false;
            currentLocked = false;
            currentPrunable = false;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                currentPath = line["worktree ".Length..];
                continue;
            }

            if (line.StartsWith("HEAD ", StringComparison.Ordinal))
            {
                currentHead = line["HEAD ".Length..];
                continue;
            }

            if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                currentBranch = line["branch ".Length..];
                continue;
            }

            if (string.Equals(line, "detached", StringComparison.Ordinal))
            {
                currentDetached = true;
                continue;
            }

            if (string.Equals(line, "bare", StringComparison.Ordinal))
            {
                currentBare = true;
                continue;
            }

            if (line.StartsWith("locked", StringComparison.Ordinal))
            {
                currentLocked = true;
                currentLockReason = line.Length > "locked ".Length ? line["locked ".Length..].Trim() : null;
                continue;
            }

            if (line.StartsWith("prunable", StringComparison.Ordinal))
            {
                currentPrunable = true;
                currentPrunableReason = line.Length > "prunable ".Length ? line["prunable ".Length..].Trim() : null;
            }
        }

        Flush();
        if (entries.Count == 0)
        {
            entries.Add(new GitWorktreeEntry(
                repositoryRoot,
                null,
                null,
                IsCurrent: true,
                IsLocked: false,
                IsPrunable: false,
                IsDetached: false,
                IsBare: false,
                LockReason: null,
                PrunableReason: null));
        }

        return entries;
    }

    private static string ResolveMainWorktreePath(ProcessRunResult commonDirResult, string repositoryRoot)
    {
        var commonDir = NormalizeLine(commonDirResult.StandardOutput);
        if (string.IsNullOrWhiteSpace(commonDir))
        {
            return repositoryRoot;
        }

        var normalizedCommonDir = NormalizeDirectorySeparators(commonDir);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var dotGitSuffix = $"{Path.DirectorySeparatorChar}.git";
        if (normalizedCommonDir.EndsWith(dotGitSuffix, comparison))
        {
            return Path.GetDirectoryName(normalizedCommonDir) ?? repositoryRoot;
        }

        var linkedMarker = $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}";
        var linkedMarkerIndex = normalizedCommonDir.LastIndexOf(linkedMarker, comparison);
        if (linkedMarkerIndex > 0)
        {
            return normalizedCommonDir[..linkedMarkerIndex];
        }

        return repositoryRoot;
    }

    private static string NormalizeDirectorySeparators(string path)
        => Path.DirectorySeparatorChar == Path.AltDirectorySeparatorChar
            ? path
            : path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static string? NormalizeBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        const string prefix = "refs/heads/";
        return branch.Trim().StartsWith(prefix, StringComparison.Ordinal)
            ? branch.Trim()[prefix.Length..]
            : branch.Trim();
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string BuildGitFailureMessage(string prefix, ProcessRunResult result)
        => string.IsNullOrWhiteSpace(result.StandardError)
            ? $"{prefix} {NormalizeMultiline(result.StandardOutput) ?? "Unknown git failure."}"
            : $"{prefix} {NormalizeMultiline(result.StandardError) ?? "Unknown git failure."}";
}
