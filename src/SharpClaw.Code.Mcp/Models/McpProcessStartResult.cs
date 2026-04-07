using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Mcp.Models;

/// <summary>
/// Captures the outcome of starting a process-hosted MCP server.
/// </summary>
/// <param name="Started">Indicates whether the process started successfully.</param>
/// <param name="Pid">The process identifier when known (legacy path; often null for SDK stdio transport).</param>
/// <param name="HandshakeSucceeded">Indicates whether the MCP session was established (initialize with the SDK).</param>
/// <param name="FailureReason">The startup or handshake failure reason, if any.</param>
/// <param name="SessionHandle">Non-zero handle for <see cref="McpProcessStopRequest"/> when the SDK client must be disposed.</param>
/// <param name="ToolCount">Discovered tools after session establishment.</param>
/// <param name="PromptCount">Discovered prompts after session establishment.</param>
/// <param name="ResourceCount">Discovered resources after session establishment.</param>
/// <param name="FailureKind">When set, overrides the default <see cref="McpFailureKind"/> inferred by the host.</param>
public sealed record McpProcessStartResult(
    bool Started,
    int? Pid,
    bool HandshakeSucceeded,
    string? FailureReason,
    long SessionHandle = 0,
    int ToolCount = 0,
    int PromptCount = 0,
    int ResourceCount = 0,
    McpFailureKind? FailureKind = null);
