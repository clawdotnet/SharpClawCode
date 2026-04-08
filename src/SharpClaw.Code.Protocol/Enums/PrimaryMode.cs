using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// High-level workflow mode for primary prompt execution (orthogonal to <see cref="PermissionMode"/>).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PrimaryMode>))]
public enum PrimaryMode
{
    /// <summary>
    /// Default execution: full tool and mutation behavior subject to permission mode.
    /// </summary>
    [JsonStringEnumMemberName("build")]
    Build,

    /// <summary>
    /// Analysis-first posture: mutating tools are restricted by policy.
    /// </summary>
    [JsonStringEnumMemberName("plan")]
    Plan,

    /// <summary>
    /// Spec-generation posture: prompt execution produces structured product specs and persists them as workspace documents.
    /// </summary>
    [JsonStringEnumMemberName("spec")]
    Spec,
}
