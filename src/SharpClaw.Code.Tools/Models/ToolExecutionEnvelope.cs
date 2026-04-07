using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Tools.Models;

/// <summary>
/// Captures the request, permission decision, and result for a single tool execution.
/// </summary>
/// <param name="Request">The tool execution request.</param>
/// <param name="PermissionDecision">The permission decision that governed execution.</param>
/// <param name="Result">The resulting tool output.</param>
public sealed record ToolExecutionEnvelope(
    ToolExecutionRequest Request,
    PermissionDecision PermissionDecision,
    ToolResult Result);
