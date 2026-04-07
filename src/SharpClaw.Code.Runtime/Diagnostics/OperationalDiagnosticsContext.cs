using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Normalized context passed to individual operational checks.
/// </summary>
public sealed record OperationalDiagnosticsContext(
    string NormalizedWorkspacePath,
    string? Model,
    PermissionMode PermissionMode);
