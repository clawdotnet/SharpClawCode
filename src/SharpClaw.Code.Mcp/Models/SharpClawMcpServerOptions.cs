using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Models;

/// <summary>Explicit host-selected permissions for inbound clients; tool arguments cannot override them.</summary>
/// <param name="WorkspaceRoot">Fixed workspace boundary.</param>
/// <param name="PermissionMode">Permission mode, read-only by default.</param>
/// <param name="AllowMutations">Whether permitted rename is advertised and callable.</param>
/// <param name="ApprovalSettings">Host-approved bounded elevation scopes.</param>
/// <param name="HostContext">Optional tenant-aware durable storage context.</param>
/// <param name="Transport">Stdio or Streamable HTTP.</param>
/// <param name="Host">Explicit IP address or localhost; wildcard binding is rejected.</param>
/// <param name="Port">HTTP listen port.</param>
/// <param name="AllowRemote">Explicit authorization for non-loopback exposure.</param>
/// <param name="BearerToken">Authentication secret for remote or elevated HTTP, supplied by the host.</param>
/// <param name="PrimaryMode">Host-selected workflow restrictions forwarded to the permission policy.</param>
public sealed record SharpClawMcpServerOptions(
    string WorkspaceRoot,
    PermissionMode PermissionMode = PermissionMode.ReadOnly,
    bool AllowMutations = false,
    ApprovalSettings? ApprovalSettings = null,
    RuntimeHostContext? HostContext = null,
    SharpClawMcpTransport Transport = SharpClawMcpTransport.Stdio,
    string Host = "127.0.0.1",
    int Port = 7346,
    bool AllowRemote = false,
    string? BearerToken = null,
    PrimaryMode PrimaryMode = PrimaryMode.Build);

/// <summary>The supported inbound transport choices.</summary>
public enum SharpClawMcpTransport
{
    /// <summary>Standard input/output with protocol-only stdout.</summary>
    Stdio,
    /// <summary>Stateful Streamable HTTP with bounded idle client lifecycle.</summary>
    Http,
}
