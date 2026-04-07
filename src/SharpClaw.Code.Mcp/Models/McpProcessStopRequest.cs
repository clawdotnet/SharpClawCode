namespace SharpClaw.Code.Mcp.Models;

/// <summary>
/// Identifies a process-hosted MCP session for shutdown (SDK session handle and/or legacy PID).
/// </summary>
/// <param name="Pid">Optional OS process id from persisted status (fallback when no SDK handle).</param>
/// <param name="SessionHandle">
/// Opaque handle matching <see cref="McpProcessStartResult.SessionHandle"/> for the active SDK client session.
/// Zero means unused.
/// </param>
public readonly record struct McpProcessStopRequest(int? Pid, long SessionHandle = 0);
