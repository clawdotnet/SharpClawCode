using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Operational;

/// <summary>
/// Machine-stable <c>status</c> command payload.
/// </summary>
/// <param name="SchemaVersion">Contract version.</param>
/// <param name="GeneratedAtUtc">When the report was built.</param>
/// <param name="WorkspaceRoot">Normalized workspace path.</param>
/// <param name="RuntimeState">High-level host state.</param>
/// <param name="SelectedModel">Effective model id if known.</param>
/// <param name="PermissionMode">Active permission mode.</param>
/// <param name="PrimaryMode">Effective primary workflow mode.</param>
/// <param name="LatestSessionId">Latest session id, if any.</param>
/// <param name="LatestSessionState">Latest session lifecycle state, if any.</param>
/// <param name="McpServerCount">Registered MCP servers.</param>
/// <param name="McpReadyCount">Servers in ready state.</param>
/// <param name="McpFaultedCount">Servers faulted.</param>
/// <param name="PluginInstalledCount">Installed plugins.</param>
/// <param name="PluginEnabledCount">Enabled plugins.</param>
/// <param name="Checks">Lightweight health snapshot (subset of doctor).</param>
public sealed record RuntimeStatusReport(
    string SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string WorkspaceRoot,
    string RuntimeState,
    string? SelectedModel,
    PermissionMode PermissionMode,
    PrimaryMode PrimaryMode,
    string? LatestSessionId,
    SessionLifecycleState? LatestSessionState,
    int McpServerCount,
    int McpReadyCount,
    int McpFaultedCount,
    int PluginInstalledCount,
    int PluginEnabledCount,
    IReadOnlyList<OperationalCheckItem> Checks);
