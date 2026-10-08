using System.ComponentModel;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Runtime.Verification;

/// <summary>Builds/tests an authorized workspace with bounded reports, explicit restore, and serialized process execution.</summary>
public sealed class VerificationService(IProcessRunner processes, IFileSystem files, IPathService paths, ISystemClock clock, IDotNetWorkspaceTargetResolver targets, IDotNetWorkspaceSemanticService semantics, IAffectedProjectResolver affected, IDotNetDiagnosticParser diagnostics, ITestResultParser trx, IGitWorkspaceService git, IVerificationDiagnosticsCache reportCache) : IVerificationService
{
    /// <inheritdoc />
    public async Task<VerificationRunReport> VerifyAsync(VerificationRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Scope) || request.TimeoutSeconds is < 1 or > 3600 || string.IsNullOrWhiteSpace(request.Configuration)) throw new ArgumentException("Invalid verification scope, timeout or configuration.");
        var started = clock.UtcNow;
        var target = await targets.ResolveAsync(new(request.WorkspaceRoot, request.Target, request.Configuration, request.TargetFramework), cancellationToken).ConfigureAwait(false);
        var root = target.WorkspaceRoot;
        var targetName = target.TargetPath is null ? null : Path.GetRelativePath(root, target.TargetPath).Replace('\\', '/');
        string[] selected = [];
        VerificationBuildReport? build = null;
        VerificationTestRunReport? tests = null;
        VerificationStepReport? restore = null;
        VerificationRunReport Finish(VerificationStatus status, VerificationFailureReason reason, string summary)
        {
            var completed = clock.UtcNow;
            var report = new VerificationRunReport(root, targetName, request.Scope, status, reason, started, completed, (long)(completed - started).TotalMilliseconds, selected, build, tests, restore, summary);
            reportCache.Store(report);
            return report;
        }
        if (target.Status != DotNetWorkspaceStatus.Ready) return Finish(target.Status == DotNetWorkspaceStatus.NoTarget ? VerificationStatus.Skipped : VerificationStatus.Failed, Map(target.Status), target.Message ?? target.Status.ToString());
        var context = new ToolExecutionContext("", "", root, root, PermissionMode.DangerFullAccess, OutputFormat.Json, null);
        var resolver = new WorkspacePathResolver(paths);
        await using var workspaceLock = await files.AcquireExclusiveFileLockAsync(resolver.ResolvePath(context, ".sharpclaw/locks/verification.lock"), cancellationToken).ConfigureAwait(false);
        if (request.Restore)
        {
            restore = await RunAsync("restore", targetName!, ["restore", targetName!, "--nologo", "--disable-build-servers"], root, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (restore.Status != VerificationStatus.Passed) return Finish(VerificationStatus.Failed, restore.Reason, "Explicit restore failed.");
        }
        DotNetSolutionSummary workspace;
        using (var evaluationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            evaluationTimeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
            try { workspace = await semantics.InspectSolutionAsync(new(root, targetName, request.Configuration, request.TargetFramework), new AuthorizedEvaluation(), evaluationTimeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Finish(VerificationStatus.Failed, VerificationFailureReason.Timeout, "Workspace evaluation timed out."); }
        }
        if (workspace.Status is not (DotNetWorkspaceStatus.Ready or DotNetWorkspaceStatus.Partial)) return Finish(VerificationStatus.Failed, Map(workspace.Status), string.Join("; ", workspace.Messages));
        if (workspace.Status == DotNetWorkspaceStatus.Partial && request.Scope != VerificationScope.Build) return Finish(VerificationStatus.Failed, VerificationFailureReason.LoadFailed, "Partial workspace loading cannot establish complete test coverage: " + string.Join("; ", workspace.Messages));
        var changed = request.ChangedPaths;
        if (request.Scope == VerificationScope.Affected && changed is null)
        {
            try
            {
                var snapshot = await git.GetSnapshotAsync(root, cancellationToken).ConfigureAwait(false);
                if (snapshot.IsRepository && snapshot.RepositoryRoot is not null)
                {
                    changed = snapshot.StatusEntries.Select(entry => Path.GetRelativePath(root, Path.Combine(snapshot.RepositoryRoot, entry.Path)).Replace('\\', '/')).ToArray();
                }
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or Win32Exception) { changed = null; }
        }
        var selection = request.Scope == VerificationScope.Affected ? affected.Resolve(workspace, changed) : new AffectedProjectSelection(workspace.Projects.Select(project => project.Path).ToArray(), true);
        selected = selection.Projects;
        if (request.Scope != VerificationScope.Tests)
        {
            var steps = new List<VerificationStepReport>();
            var allDiagnostics = new List<VerificationDiagnostic>();
            var buildTargets = request.Scope == VerificationScope.Affected && !selection.ConservativeFallback ? selected : [targetName!];
            foreach (var buildTarget in buildTargets)
            {
                var arguments = new List<string> { "build", buildTarget, "--nologo", "--no-restore", "--configuration", request.Configuration, "--disable-build-servers" };
                if (request.TargetFramework is not null) arguments.AddRange(["--framework", request.TargetFramework]);
                var step = await RunAsync("build", buildTarget, arguments, root, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
                steps.Add(step);
                allDiagnostics.AddRange(step.Diagnostics ?? []);
                if (step.Status != VerificationStatus.Passed)
                {
                    build = new(VerificationStatus.Failed, steps.ToArray(), allDiagnostics.Distinct().ToArray());
                    return Finish(VerificationStatus.Failed, step.Reason, "Build failed; tests were not run.");
                }
            }
            build = new(VerificationStatus.Passed, steps.ToArray(), allDiagnostics.Distinct().ToArray());
        }
        if (request.Scope == VerificationScope.Build) return Finish(VerificationStatus.Passed, VerificationFailureReason.None, "Build passed.");
        var testProjects = workspace.Projects.Where(project => project.IsTestProject && selected.Contains(project.Path, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)).ToArray();
        if (testProjects.Length == 0)
        {
            tests = new(VerificationStatus.Skipped, VerificationFailureReason.NoTestProjects, 0, 0, 0, 0, [], [], ["No test projects were selected."]);
            return Finish(build is null ? VerificationStatus.Skipped : VerificationStatus.Passed, VerificationFailureReason.NoTestProjects, build is null ? "No test projects; verification skipped." : "Build passed; no test projects were selected.");
        }
        var resultRoot = resolver.ResolvePath(context, ".sharpclaw/verification/" + Guid.NewGuid().ToString("N"));
        var reports = new List<VerificationTestRunReport>();
        var testSteps = new List<VerificationStepReport>();
        try
        {
            foreach (var project in testProjects)
            {
                if (request.Scope == VerificationScope.Tests && (project.OutputPath is null || !files.FileExists(resolver.ResolvePath(context, project.OutputPath))))
                {
                    tests = Merge(reports, testSteps, VerificationStatus.Failed, VerificationFailureReason.TestOutputUnavailable, ["Build the selected test project/configuration before using tests scope: " + project.Path]);
                    return Finish(VerificationStatus.Failed, VerificationFailureReason.TestOutputUnavailable, "Test output is unavailable; tests scope does not build.");
                }
                var resultDirectory = Path.Combine(resultRoot, Guid.NewGuid().ToString("N"));
                files.CreateDirectory(resultDirectory);
                var arguments = new List<string> { "test", project.Path, "--no-build", "--no-restore", "--configuration", request.Configuration, "--logger", "trx", "--results-directory", resultDirectory, "--nologo", "--disable-build-servers" };
                if (request.TargetFramework is not null) arguments.AddRange(["--framework", request.TargetFramework]);
                var step = await RunAsync("test", project.Path, arguments, root, request.TimeoutSeconds, cancellationToken).ConfigureAwait(false);
                testSteps.Add(step);
                var reportPaths = files.EnumerateFiles(resultDirectory, "*.trx").ToArray();
                foreach (var reportPath in reportPaths)
                {
                    var report = await files.ReadAllTextIfExistsAsync(reportPath, cancellationToken).ConfigureAwait(false);
                    reports.Add(trx.Parse(report ?? "", project.Path, request.TargetFramework ?? (project.TargetFrameworks.Length == 1 ? project.EvaluatedTargetFramework : null)));
                }
                if (reportPaths.Length == 0 || step.Status != VerificationStatus.Passed || reports.Any(report => report.Status != VerificationStatus.Passed))
                {
                    var reason = step.Status != VerificationStatus.Passed ? step.Reason : VerificationFailureReason.TestResultsMissing;
                    if (reports.Any(report => report.Failed > 0)) reason = VerificationFailureReason.TestFailed;
                    tests = Merge(reports, testSteps, VerificationStatus.Failed, reason, reportPaths.Length == 0 ? ["Expected TRX test results are missing: " + project.Path] : []);
                    return Finish(VerificationStatus.Failed, reason, "Test verification failed or was incomplete.");
                }
            }
            tests = Merge(reports, testSteps, VerificationStatus.Passed, VerificationFailureReason.None, []);
            return Finish(VerificationStatus.Passed, VerificationFailureReason.None, "Build/test verification passed.");
        }
        finally
        {
            try { files.DeleteDirectoryRecursive(resultRoot); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Only this unique run directory is eligible for best-effort cleanup. */ }
        }
    }

    private async Task<VerificationStepReport> RunAsync(string kind, string target, IReadOnlyList<string> arguments, string root, int timeoutSeconds, CancellationToken caller)
    {
        var started = clock.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(caller);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            var result = await processes.RunAsync(new ProcessRunRequest("dotnet", arguments.ToArray(), root, new Dictionary<string, string?> { ["DOTNET_NOLOGO"] = "1", ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1" }), timeout.Token).ConfigureAwait(false);
            var reason = result.ExitCode == 0 ? VerificationFailureReason.None : Classify(result.StandardOutput + result.StandardError, kind);
            return new(kind, target, result.ExitCode == 0 ? VerificationStatus.Passed : VerificationStatus.Failed, reason, result.ExitCode, started, clock.UtcNow, (long)(clock.UtcNow - started).TotalMilliseconds, DotNetDiagnosticParser.Bound(result.StandardOutput, 16000), DotNetDiagnosticParser.Bound(result.StandardError, 16000), diagnostics.Parse(result.StandardOutput, result.StandardError, root));
        }
        catch (OperationCanceledException) when (!caller.IsCancellationRequested)
        {
            return Failure(VerificationFailureReason.Timeout, "Verification step timed out.");
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidOperationException)
        {
            return Failure(error is Win32Exception ? VerificationFailureReason.SdkUnavailable : VerificationFailureReason.ProcessFailed, error.Message);
        }
        VerificationStepReport Failure(VerificationFailureReason reason, string error) => new(kind, target, VerificationStatus.Failed, reason, null, started, clock.UtcNow, (long)(clock.UtcNow - started).TotalMilliseconds, "", error);
    }
    private static VerificationFailureReason Classify(string text, string kind)
    {
        if (text.Contains("NETSDK1004", StringComparison.Ordinal) || text.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase)) return VerificationFailureReason.RestoreRequired;
        if (text.Contains("SDK", StringComparison.OrdinalIgnoreCase) && text.Contains("not found", StringComparison.OrdinalIgnoreCase)) return VerificationFailureReason.SdkUnavailable;
        return kind == "test" ? VerificationFailureReason.TestFailed : kind == "build" ? VerificationFailureReason.BuildFailed : VerificationFailureReason.ProcessFailed;
    }
    private static VerificationFailureReason Map(DotNetWorkspaceStatus status) => status switch
    {
        DotNetWorkspaceStatus.NoTarget => VerificationFailureReason.NoTarget,
        DotNetWorkspaceStatus.AmbiguousTarget => VerificationFailureReason.AmbiguousTarget,
        DotNetWorkspaceStatus.InvalidTarget => VerificationFailureReason.InvalidTarget,
        DotNetWorkspaceStatus.PermissionDenied => VerificationFailureReason.PermissionDenied,
        DotNetWorkspaceStatus.SdkUnavailable => VerificationFailureReason.SdkUnavailable,
        DotNetWorkspaceStatus.RestoreRequired => VerificationFailureReason.RestoreRequired,
        _ => VerificationFailureReason.LoadFailed
    };
    private static VerificationTestRunReport Merge(List<VerificationTestRunReport> reports, List<VerificationStepReport> steps, VerificationStatus status, VerificationFailureReason reason, string[] messages)
        => new(status, reason, reports.Sum(report => report.Total), reports.Sum(report => report.Passed), reports.Sum(report => report.Failed), reports.Sum(report => report.Skipped), reports.SelectMany(report => report.Cases).OrderBy(item => item.Outcome == "Failed" ? 0 : 1).Take(500).ToArray(), steps.ToArray(), [.. reports.SelectMany(report => report.Messages), .. messages]);
    private sealed class AuthorizedEvaluation : IDotNetWorkspaceEvaluationAuthorization
    {
        public Task<bool> AuthorizeAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(true); }
    }
}
