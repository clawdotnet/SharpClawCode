using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Mcp.Abstractions;

/// <summary>
/// Produces structured MCP diagnostics and status summaries.
/// </summary>
public interface IMcpDoctorService
{
    /// <summary>
    /// Builds a status summary for all or one registered MCP server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The optional server identifier to inspect.</param>
    /// <param name="outputFormat">The requested output format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A normalized command result.</returns>
    Task<CommandResult> GetStatusAsync(string workspaceRoot, string? serverId, OutputFormat outputFormat, CancellationToken cancellationToken);

    /// <summary>
    /// Runs basic MCP diagnostics for the workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="outputFormat">The requested output format.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A normalized command result.</returns>
    Task<CommandResult> RunDoctorAsync(string workspaceRoot, OutputFormat outputFormat, CancellationToken cancellationToken);
}
