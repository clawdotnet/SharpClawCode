using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Tools.Models;

/// <summary>
/// Describes a discoverable SharpClaw tool.
/// </summary>
/// <param name="Name">The stable tool name.</param>
/// <param name="Description">A concise description of the tool.</param>
/// <param name="ApprovalScope">The approval scope enforced for this tool.</param>
/// <param name="IsDestructive">Indicates whether the tool mutates workspace or environment state.</param>
/// <param name="RequiresApproval">Indicates whether the tool normally requires approval outside full-trust mode.</param>
/// <param name="InputTypeName">The CLR argument contract type name used by the tool.</param>
/// <param name="InputDescription">A concise description of the JSON input shape.</param>
/// <param name="Tags">Searchable tags for discoverability.</param>
public sealed record ToolDefinition(
    string Name,
    string Description,
    ApprovalScope ApprovalScope,
    bool IsDestructive,
    bool RequiresApproval,
    string InputTypeName,
    string InputDescription,
    string[] Tags);
