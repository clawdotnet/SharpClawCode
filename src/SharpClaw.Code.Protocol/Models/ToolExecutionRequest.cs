using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a tool execution request emitted by the runtime.
/// </summary>
/// <param name="Id">The unique tool request identifier.</param>
/// <param name="SessionId">The parent session identifier.</param>
/// <param name="TurnId">The parent turn identifier.</param>
/// <param name="ToolName">The tool name or id to execute.</param>
/// <param name="ArgumentsJson">The JSON-encoded tool arguments.</param>
/// <param name="ApprovalScope">The approval scope required by the request.</param>
/// <param name="WorkingDirectory">The working directory for execution, if any.</param>
/// <param name="RequiresApproval">Indicates whether interactive approval is required.</param>
/// <param name="IsDestructive">Indicates whether the action mutates workspace or environment state.</param>
public sealed record ToolExecutionRequest(
    string Id,
    string SessionId,
    string TurnId,
    string ToolName,
    string ArgumentsJson,
    ApprovalScope ApprovalScope,
    string? WorkingDirectory,
    bool RequiresApproval,
    bool IsDestructive);
