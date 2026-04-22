using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Input for operational diagnostics (doctor/status).
/// </summary>
/// <param name="WorkingDirectory">Raw working directory from CLI.</param>
/// <param name="Model">Selected model id, if any.</param>
/// <param name="PermissionMode">Permission mode.</param>
/// <param name="OutputFormat">Requested output format.</param>
/// <param name="PrimaryMode">Optional primary mode hint; session metadata wins when null.</param>
/// <param name="ApprovalSettings">Optional bounded auto-approval settings.</param>
public sealed record OperationalDiagnosticsInput(
    string WorkingDirectory,
    string? Model,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    PrimaryMode? PrimaryMode = null,
    ApprovalSettings? ApprovalSettings = null);
