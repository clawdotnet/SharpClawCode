using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents the current status of an MCP server.
/// </summary>
/// <param name="ServerId">The MCP server identifier.</param>
/// <param name="State">The current MCP lifecycle state.</param>
/// <param name="UpdatedAtUtc">The UTC timestamp when the status was last refreshed.</param>
/// <param name="StatusMessage">A status or health message, if available.</param>
/// <param name="ToolCount">The number of discovered tools exposed by the server.</param>
/// <param name="IsHealthy">Indicates whether the server is currently considered healthy.</param>
/// <param name="Pid">The process identifier for process-hosted servers, if any.</param>
/// <param name="FailureKind">The most recent typed failure category.</param>
/// <param name="HandshakeSucceeded">Indicates whether the MCP session was established (SDK initialize completed).</param>
/// <param name="SessionHandle">Opaque handle for the active SDK client session; null when stopped or non-SDK.</param>
/// <param name="PromptCount">The number of discovered prompts.</param>
/// <param name="ResourceCount">The number of discovered resources.</param>
public sealed record McpServerStatus(
    string ServerId,
    McpLifecycleState State,
    DateTimeOffset UpdatedAtUtc,
    string? StatusMessage,
    int ToolCount,
    bool IsHealthy,
    int? Pid = null,
    McpFailureKind FailureKind = McpFailureKind.None,
    bool HandshakeSucceeded = false,
    long? SessionHandle = null,
    int PromptCount = 0,
    int ResourceCount = 0)
{
    /// <summary>The current MCP lifecycle state. Reaching <see cref="McpLifecycleState.Ready"/> requires <see cref="HandshakeSucceeded"/> to be true.</summary>
    public McpLifecycleState State { get; init; } = State == McpLifecycleState.Ready && !HandshakeSucceeded
        ? throw new ArgumentException("McpServerStatus.State cannot be Ready unless HandshakeSucceeded is true.", nameof(State))
        : State;
}
