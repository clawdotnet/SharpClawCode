using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Represents a request for explicit approval.
/// </summary>
/// <param name="Scope">The permission scope requiring approval.</param>
/// <param name="ToolName">The tool name being approved.</param>
/// <param name="Prompt">The user-facing approval prompt.</param>
/// <param name="RequestedBy">The actor or subsystem requesting approval.</param>
/// <param name="CanRememberDecision">Indicates whether a positive approval can be remembered for the session.</param>
public sealed record ApprovalRequest(
    ApprovalScope Scope,
    string ToolName,
    string Prompt,
    string RequestedBy,
    bool CanRememberDecision);
