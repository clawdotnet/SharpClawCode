using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Services;

/// <summary>
/// Provides basic MCP diagnostics and structured status summaries.
/// </summary>
public sealed class McpDoctorService(IMcpRegistry registry) : IMcpDoctorService
{
    /// <inheritdoc />
    public async Task<CommandResult> GetStatusAsync(string workspaceRoot, string? serverId, OutputFormat outputFormat, CancellationToken cancellationToken)
    {
        var servers = await registry.ListAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var filteredServers = string.IsNullOrWhiteSpace(serverId)
            ? servers.ToArray()
            : servers.Where(server => string.Equals(server.Definition.Id, serverId, StringComparison.OrdinalIgnoreCase)).ToArray();

        var payloadModel = new McpDoctorStatusPayload(
            workspaceRoot,
            filteredServers.Length,
            filteredServers.Select(server => new McpDoctorServerEntry(
                Id: server.Definition.Id,
                DisplayName: server.Definition.DisplayName,
                Transport: server.Definition.TransportKind,
                Endpoint: server.Definition.Endpoint,
                State: server.Status.State.ToString(),
                Pid: server.Status.Pid,
                SessionHandle: server.Status.SessionHandle,
                ToolCount: server.Status.ToolCount,
                PromptCount: server.Status.PromptCount,
                ResourceCount: server.Status.ResourceCount,
                FailureKind: server.Status.FailureKind,
                HandshakeSucceeded: server.Status.HandshakeSucceeded,
                Message: server.Status.StatusMessage)).ToList());

        var payload = JsonSerializer.Serialize(payloadModel, McpDoctorJsonContext.Default.McpDoctorStatusPayload);

        var message = filteredServers.Length == 0
            ? "No MCP servers are registered for this workspace."
            : string.Join(
                Environment.NewLine,
                filteredServers.Select(server =>
                    $"{server.Definition.Id}: {server.Status.State}"
                    + (server.Status.FailureKind is not McpFailureKind.None ? $" ({FormatFailureKind(server.Status.FailureKind)})" : string.Empty)
                    + (string.IsNullOrWhiteSpace(server.Status.StatusMessage) ? string.Empty : $" - {server.Status.StatusMessage}")));

        return new CommandResult(
            Succeeded: true,
            ExitCode: 0,
            OutputFormat: outputFormat,
            Message: message,
            DataJson: payload);
    }

    /// <inheritdoc />
    public async Task<CommandResult> RunDoctorAsync(string workspaceRoot, OutputFormat outputFormat, CancellationToken cancellationToken)
    {
        var servers = await registry.ListAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var checks = servers.Select(ToCheck).ToList();
        var overallStatus = checks.Any(check => check.Status == "faulted") ? "faulted" : "ok";
        var payloadModel = new McpDoctorReportPayload(workspaceRoot, overallStatus, checks);
        var payload = JsonSerializer.Serialize(payloadModel, McpDoctorJsonContext.Default.McpDoctorReportPayload);

        var message = checks.Count == 0
            ? "No MCP servers are registered."
            : string.Join(Environment.NewLine, checks.Select(check => $"[{check.Status}] {check.Name}: {check.Detail}"));

        return new CommandResult(
            Succeeded: overallStatus != "faulted",
            ExitCode: overallStatus == "faulted" ? 1 : 0,
            OutputFormat: outputFormat,
            Message: message,
            DataJson: payload);
    }

    private static McpDoctorCheck ToCheck(RegisteredMcpServer server)
        => new(
            server.Definition.Id,
            server.Status.State is McpLifecycleState.Faulted ? "faulted" : "ok",
            $"{server.Status.State} - {server.Status.StatusMessage ?? "No status message."}");

    private static string FormatFailureKind(McpFailureKind failureKind)
        => failureKind switch
        {
            McpFailureKind.None => "none",
            McpFailureKind.Startup => "startup",
            McpFailureKind.Handshake => "handshake",
            McpFailureKind.Capabilities => "capabilities",
            McpFailureKind.Runtime => "runtime",
            _ => failureKind.ToString()
        };
}

/// <summary>Source-generated JSON context for MCP doctor payloads.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(McpDoctorStatusPayload))]
[JsonSerializable(typeof(McpDoctorReportPayload))]
internal sealed partial class McpDoctorJsonContext : JsonSerializerContext;
