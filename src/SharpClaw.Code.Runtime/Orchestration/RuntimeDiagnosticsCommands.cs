using System.Text.Json;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Diagnostics;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Status, doctor, and session-inspection commands. Stateless over the operational diagnostics coordinator;
/// <see cref="ConversationRuntime"/> exposes them through <see cref="IRuntimeCommandService"/>.
/// </summary>
public sealed class RuntimeDiagnosticsCommands(
    IRuntimeHostContextAccessor hostContextAccessor,
    IOperationalDiagnosticsCoordinator operationalDiagnostics)
{
    /// <summary>Builds the runtime status report for the command context.</summary>
    public async Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.ApprovalSettings);
        var report = await operationalDiagnostics
            .BuildStatusReportAsync(input, cancellationToken)
            .ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(report, ProtocolJsonContext.Default.RuntimeStatusReport);
        var message = FormatStatusMessage(report);

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <summary>Runs doctor checks; exits 1 when any check reports an error.</summary>
    public async Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.ApprovalSettings);
        var report = await operationalDiagnostics.RunDoctorAsync(input, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(report, ProtocolJsonContext.Default.DoctorReport);
        var exitCode = report.OverallStatus == OperationalCheckStatus.Error ? 1 : 0;
        var message = FormatDoctorMessage(report);

        return new CommandResult(
            Succeeded: exitCode == 0,
            ExitCode: exitCode,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <summary>Inspects one session, or the latest when <paramref name="sessionId"/> is null.</summary>
    public async Task<CommandResult> InspectSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var input = new OperationalDiagnosticsInput(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.ApprovalSettings);
        var inspection = await operationalDiagnostics
            .InspectSessionAsync(sessionId, input, cancellationToken)
            .ConfigureAwait(false);
        if (inspection is null)
        {
            return new CommandResult(
                Succeeded: false,
                ExitCode: 1,
                OutputFormat: context.OutputFormat,
                Message: "No matching session found.",
                DataJson: null);
        }

        var payload = JsonSerializer.Serialize(inspection, ProtocolJsonContext.Default.SessionInspectionReport);
        var message =
            $"{inspection.Session.Id} ({inspection.Session.State}) · {inspection.PersistedEventCount} persisted events";

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: context.OutputFormat,
            Message: message,
            DataJson: payload);
    }

    private static string FormatDoctorMessage(DoctorReport report)
        => string.Join(
            Environment.NewLine,
            report.Checks.Select(static c =>
            {
                var detail = string.IsNullOrWhiteSpace(c.Detail) ? string.Empty : $" — {c.Detail}";
                return $"[{c.Status}] {c.Id}: {c.Summary}{detail}";
            }));

    private static string FormatStatusMessage(RuntimeStatusReport report)
    {
        var sessionLabel = report.LatestSessionId is null
            ? "no session"
            : $"{report.LatestSessionId} ({report.LatestSessionState})";
        var approvalLabel = report.ApprovalSettings is null
            ? "manual approvals"
            : report.ApprovalSettings.AutoApproveScopes.Count == 0
                ? "manual approvals"
                : report.ApprovalSettings.AutoApproveBudget is { } budget
                    ? $"auto-approve {string.Join(',', report.ApprovalSettings.AutoApproveScopes)} (budget {budget})"
                    : $"auto-approve {string.Join(',', report.ApprovalSettings.AutoApproveScopes)}";
        var headline =
            $"Ready · {report.WorkspaceRoot} · model {report.SelectedModel} · mode {report.PrimaryMode} · approvals {approvalLabel} · latest {sessionLabel} · "
            + $"MCP {report.McpReadyCount}/{report.McpServerCount} · plugins {report.PluginEnabledCount}/{report.PluginInstalledCount} · "
            + $"LSP {report.LspServerCount} · diagnostics {report.DiagnosticsErrorCount} error(s), {report.DiagnosticsWarningCount} warning(s)";
        var notable = report.Checks.FirstOrDefault(static c
            => c.Status is not OperationalCheckStatus.Ok and not OperationalCheckStatus.Skipped);
        return notable is null ? headline : $"{headline}{Environment.NewLine}[{notable.Status}] {notable.Id}: {notable.Summary}";
    }
}
