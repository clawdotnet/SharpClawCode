using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Input for operational diagnostics (doctor/status).
/// </summary>
/// <param name="WorkingDirectory">Raw working directory from CLI.</param>
/// <param name="Model">Selected model id, if any.</param>
/// <param name="PermissionMode">Permission mode.</param>
/// <param name="OutputFormat">Requested output format.</param>
/// <param name="PrimaryMode">Optional primary mode hint; session metadata wins when null.</param>
public sealed record OperationalDiagnosticsInput(
    string WorkingDirectory,
    string? Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    PrimaryMode? PrimaryMode = null);
