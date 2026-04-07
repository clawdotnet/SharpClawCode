using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Represents the lifecycle state of a conversation session.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SessionLifecycleState>))]
public enum SessionLifecycleState
{
    /// <summary>
    /// The session has been created but has not processed work yet.
    /// </summary>
    [JsonStringEnumMemberName("created")]
    Created,

    /// <summary>
    /// The session is actively processing or ready to process turns.
    /// </summary>
    [JsonStringEnumMemberName("active")]
    Active,

    /// <summary>
    /// The session is paused and can be resumed later.
    /// </summary>
    /// <remarks>Reserved for future workflows; the default runtime state machine does not emit this state yet.</remarks>
    [JsonStringEnumMemberName("paused")]
    Paused,

    /// <summary>
    /// The session is attempting recovery from an interruption or failure.
    /// </summary>
    [JsonStringEnumMemberName("recovering")]
    Recovering,

    /// <summary>
    /// The session finished successfully.
    /// </summary>
    /// <remarks>Reserved for future workflows; the default runtime state machine does not emit this state yet.</remarks>
    [JsonStringEnumMemberName("completed")]
    Completed,

    /// <summary>
    /// The session ended due to a terminal failure.
    /// </summary>
    [JsonStringEnumMemberName("failed")]
    Failed,

    /// <summary>
    /// The session has been archived for durable retention.
    /// </summary>
    [JsonStringEnumMemberName("archived")]
    Archived,
}
