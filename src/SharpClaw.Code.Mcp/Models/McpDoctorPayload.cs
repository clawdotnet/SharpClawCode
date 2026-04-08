using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Mcp.Models;

/// <summary>Status payload returned by <see cref="Services.McpDoctorService.GetStatusAsync"/>.</summary>
public sealed record McpDoctorStatusPayload(
    string WorkspaceRoot,
    int ServerCount,
    IReadOnlyList<McpDoctorServerEntry> Servers);

/// <summary>Per-server entry inside a doctor status payload.</summary>
public sealed record McpDoctorServerEntry(
    string Id,
    string DisplayName,
    string Transport,
    string Endpoint,
    string State,
    int? Pid,
    long? SessionHandle,
    int ToolCount,
    int PromptCount,
    int ResourceCount,
    McpFailureKind FailureKind,
    bool HandshakeSucceeded,
    string? Message);

/// <summary>Aggregate result returned by <see cref="Services.McpDoctorService.RunDoctorAsync"/>.</summary>
public sealed record McpDoctorReportPayload(
    string WorkspaceRoot,
    string OverallStatus,
    IReadOnlyList<McpDoctorCheck> Checks);

/// <summary>A single MCP doctor check entry.</summary>
public sealed record McpDoctorCheck(string Name, string Status, string Detail);
