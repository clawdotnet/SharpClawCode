using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// A single modular doctor/status diagnostic.
/// </summary>
public interface IOperationalCheck
{
    /// <summary>
    /// Stable id (e.g. <c>workspace.access</c>).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Runs the check.
    /// </summary>
    Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken);
}
