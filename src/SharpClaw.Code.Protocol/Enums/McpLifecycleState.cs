using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Represents the lifecycle state of an MCP server integration.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<McpLifecycleState>))]
public enum McpLifecycleState
{
    /// <summary>
    /// The server has been defined but not configured for use.
    /// </summary>
    [JsonStringEnumMemberName("unconfigured")]
    Unconfigured,

    /// <summary>
    /// The server is intentionally disabled.
    /// </summary>
    [JsonStringEnumMemberName("disabled")]
    Disabled,

    /// <summary>
    /// The server is currently starting.
    /// </summary>
    [JsonStringEnumMemberName("starting")]
    Starting,

    /// <summary>
    /// The server is currently connecting or starting.
    /// </summary>
    [JsonStringEnumMemberName("connecting")]
    Connecting,

    /// <summary>
    /// The server is healthy and available.
    /// </summary>
    [JsonStringEnumMemberName("ready")]
    Ready,

    /// <summary>
    /// The server is available with partial capability or degraded health.
    /// </summary>
    [JsonStringEnumMemberName("degraded")]
    Degraded,

    /// <summary>
    /// The server is intentionally stopped.
    /// </summary>
    [JsonStringEnumMemberName("stopped")]
    Stopped,

    /// <summary>
    /// The server is in a faulted state.
    /// </summary>
    [JsonStringEnumMemberName("faulted")]
    Faulted,
}
