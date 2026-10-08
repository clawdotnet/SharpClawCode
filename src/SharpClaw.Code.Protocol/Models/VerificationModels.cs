namespace SharpClaw.Code.Protocol.Models;

/// <summary>The build/test coverage requested by a caller.</summary>
public enum VerificationScope
{
    /// <summary>Build affected projects and test their reverse dependency closure.</summary>
    Affected,
    /// <summary>Build the selected solution/project.</summary>
    Build,
    /// <summary>Test existing outputs without building.</summary>
    Tests,
    /// <summary>Build and test the entire selected target.</summary>
    All
}
/// <summary>A verification operation's terminal outcome.</summary>
public enum VerificationStatus
{
    /// <summary>The requested checks passed.</summary>
    Passed,
    /// <summary>A check or prerequisite failed.</summary>
    Failed,
    /// <summary>No applicable check ran.</summary>
    Skipped,
    /// <summary>The caller cancelled execution.</summary>
    Cancelled
}
/// <summary>A stable, machine-readable failure or skip explanation.</summary>
public enum VerificationFailureReason
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>No .NET target.</summary>
    NoTarget,
    /// <summary>An explicit target is needed.</summary>
    AmbiguousTarget,
    /// <summary>Invalid target or path.</summary>
    InvalidTarget,
    /// <summary>Execution permission denied.</summary>
    PermissionDenied,
    /// <summary>A compatible SDK is unavailable.</summary>
    SdkUnavailable,
    /// <summary>Restore assets are missing.</summary>
    RestoreRequired,
    /// <summary>Workspace evaluation failed or was incomplete.</summary>
    LoadFailed,
    /// <summary>Build returned a failure.</summary>
    BuildFailed,
    /// <summary>Tests failed.</summary>
    TestFailed,
    /// <summary>Required test outputs are unavailable.</summary>
    TestOutputUnavailable,
    /// <summary>Expected test results are absent or malformed.</summary>
    TestResultsMissing,
    /// <summary>No test project was selected.</summary>
    NoTestProjects,
    /// <summary>A step exceeded its timeout.</summary>
    Timeout,
    /// <summary>A process could not run.</summary>
    ProcessFailed,
    /// <summary>Caller cancellation.</summary>
    Cancelled
}
/// <summary>Trusted verification input. Execution must be authorized by the host before invoking the worker.</summary>
public sealed record VerificationRequest(string WorkspaceRoot, VerificationScope Scope = VerificationScope.Affected, string? Target = null, string Configuration = "Debug", string? TargetFramework = null, bool Restore = false, string[]? ChangedPaths = null, int TimeoutSeconds = 120);
/// <summary>Tool input without a caller-controlled workspace boundary.</summary>
public sealed record VerifyWorkspaceArguments(VerificationScope Scope = VerificationScope.Affected, string? Target = null, string Configuration = "Debug", string? TargetFramework = null, bool Restore = false, string[]? ChangedPaths = null, int TimeoutSeconds = 120);
/// <summary>A parsed build diagnostic with optional one-based source coordinates.</summary>
public sealed record VerificationDiagnostic(string? Path, int? Line, int? Column, string Code, string Severity, string Message, string? ProjectPath);
/// <summary>A bounded process step and its actual exit status.</summary>
public sealed record VerificationStepReport(string Kind, string Target, VerificationStatus Status, VerificationFailureReason Reason, int? ExitCode, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, long DurationMilliseconds, string Output, string Error, VerificationDiagnostic[]? Diagnostics = null);
/// <summary>Build steps and deduplicated compiler/MSBuild diagnostics.</summary>
public sealed record VerificationBuildReport(VerificationStatus Status, VerificationStepReport[] Steps, VerificationDiagnostic[] Diagnostics);
/// <summary>A TRX test case, with bounded failure details.</summary>
public sealed record VerificationTestCaseResult(string ProjectPath, string? TargetFramework, string Name, string Outcome, string? Message, string? StackTrace);
/// <summary>Aggregated test execution with explicit skipped or incomplete results.</summary>
public sealed record VerificationTestRunReport(VerificationStatus Status, VerificationFailureReason Reason, int Total, int Passed, int Failed, int Skipped, VerificationTestCaseResult[] Cases, VerificationStepReport[] Steps, string[] Messages);
/// <summary>The final verification report, independent of model/provider credentials.</summary>
public sealed record VerificationRunReport(string WorkspaceRoot, string? Target, VerificationScope Scope, VerificationStatus Status, VerificationFailureReason Reason, DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc, long DurationMilliseconds, string[] AffectedProjects, VerificationBuildReport? Build, VerificationTestRunReport? Tests, VerificationStepReport? Restore, string Summary, int RepairAttempts = 0);
/// <summary>A conservative evaluated-project selection.</summary>
public sealed record AffectedProjectSelection(string[] Projects, bool ConservativeFallback);
