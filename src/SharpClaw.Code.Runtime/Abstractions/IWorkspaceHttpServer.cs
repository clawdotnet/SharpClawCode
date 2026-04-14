using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Hosts the embedded SharpClaw HTTP server for editor and automation clients.
/// </summary>
public interface IWorkspaceHttpServer
{
    /// <summary>
    /// Runs the HTTP server until cancellation is requested.
    /// </summary>
    Task RunAsync(
        string workspaceRoot,
        string? host,
        int? port,
        RuntimeCommandContext context,
        CancellationToken cancellationToken);
}
