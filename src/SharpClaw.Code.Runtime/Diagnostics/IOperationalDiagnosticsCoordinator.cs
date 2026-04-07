using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Builds versioned operational reports for CLI commands.
/// </summary>
public interface IOperationalDiagnosticsCoordinator
{
    /// <summary>
    /// Produces a full doctor report.
    /// </summary>
    Task<DoctorReport> RunDoctorAsync(OperationalDiagnosticsInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Produces a concise status report plus a small health snapshot.
    /// </summary>
    Task<RuntimeStatusReport> BuildStatusReportAsync(OperationalDiagnosticsInput input, CancellationToken cancellationToken);

    /// <summary>
    /// Inspects a session with persisted event stats. Returns null when no matching session exists.
    /// </summary>
    Task<SessionInspectionReport?> InspectSessionAsync(string? sessionIdOrNullForLatest, OperationalDiagnosticsInput input, CancellationToken cancellationToken);
}
