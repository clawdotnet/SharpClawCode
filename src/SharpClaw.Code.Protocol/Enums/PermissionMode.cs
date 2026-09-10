using System.Text.Json.Serialization;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Describes how permission-sensitive operations should be handled.
/// </summary>
[JsonConverter(typeof(PermissionModeJsonConverter))]
public enum PermissionMode
{
    /// <summary>
    /// Restricts execution to non-destructive, read-oriented operations.
    /// </summary>
    [JsonStringEnumMemberName("readOnly")]
    ReadOnly,

    /// <summary>
    /// Allows reading and writing within the workspace while escalating elevated actions.
    /// </summary>
    [JsonStringEnumMemberName("workspaceWrite")]
    WorkspaceWrite,

    /// <summary>
    /// Allows all actions, subject to explicit dangerous-operation override rules.
    /// </summary>
    [JsonStringEnumMemberName("dangerFullAccess")]
    DangerFullAccess,
}
