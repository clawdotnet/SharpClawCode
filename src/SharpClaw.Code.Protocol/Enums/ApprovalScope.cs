using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Identifies the capability boundary covered by an approval or permission decision.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ApprovalScope>))]
public enum ApprovalScope
{
    /// <summary>
    /// No approval scope is associated with the decision.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// Approval applies to a tool execution request.
    /// </summary>
    [JsonStringEnumMemberName("toolExecution")]
    ToolExecution,

    /// <summary>
    /// Approval applies to file-system mutation.
    /// </summary>
    [JsonStringEnumMemberName("fileSystemWrite")]
    FileSystemWrite,

    /// <summary>
    /// Approval applies to shell command execution.
    /// </summary>
    [JsonStringEnumMemberName("shellExecution")]
    ShellExecution,

    /// <summary>
    /// Approval applies to outbound network access.
    /// </summary>
    [JsonStringEnumMemberName("networkAccess")]
    NetworkAccess,

    /// <summary>
    /// Approval applies to session-level operations such as recovery or deletion.
    /// </summary>
    [JsonStringEnumMemberName("sessionOperation")]
    SessionOperation,

    /// <summary>
    /// Approval applies to reading a file outside the workspace for prompt composition (for example <c>@file</c>).
    /// </summary>
    [JsonStringEnumMemberName("promptOutsideWorkspaceRead")]
    PromptOutsideWorkspaceRead,
}
