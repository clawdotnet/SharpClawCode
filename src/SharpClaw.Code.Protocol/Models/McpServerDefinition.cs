namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a configured MCP server definition known to the runtime.
/// </summary>
/// <param name="Id">The unique MCP server identifier.</param>
/// <param name="DisplayName">A human-friendly server display name.</param>
/// <param name="TransportKind">The transport kind, such as stdio or HTTP.</param>
/// <param name="Endpoint">The endpoint, command, or address used to reach the server.</param>
/// <param name="EnabledByDefault">Indicates whether the server should be enabled by default.</param>
/// <param name="Environment">Optional environment variables associated with the server.</param>
/// <param name="Arguments">Optional process arguments when the server is process-hosted.</param>
public sealed record McpServerDefinition(
    string Id,
    string DisplayName,
    string TransportKind,
    string Endpoint,
    bool EnabledByDefault,
    Dictionary<string, string>? Environment,
    string[]? Arguments = null);
