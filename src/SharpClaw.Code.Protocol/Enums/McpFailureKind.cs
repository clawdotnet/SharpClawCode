using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Describes the coarse failure category for an MCP server lifecycle transition.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<McpFailureKind>))]
public enum McpFailureKind
{
    /// <summary>
    /// No failure has been recorded.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// The server failed before a process could be started successfully.
    /// </summary>
    [JsonStringEnumMemberName("startup")]
    Startup,

    /// <summary>
    /// The server process started but failed its initial handshake.
    /// </summary>
    [JsonStringEnumMemberName("handshake")]
    Handshake,

    /// <summary>
    /// The session connected but enumerating tools, prompts, or resources failed.
    /// </summary>
    [JsonStringEnumMemberName("capabilities")]
    Capabilities,

    /// <summary>
    /// The server failed after initial startup.
    /// </summary>
    [JsonStringEnumMemberName("runtime")]
    Runtime
}
