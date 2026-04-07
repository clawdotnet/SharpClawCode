using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Summarizes MCP registry entries and reconciles with host status when possible.
/// </summary>
public sealed class McpRegistryHealthCheck(IMcpRegistry registry, IMcpServerHost? serverHost = null) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "mcp.registry";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        try
        {
            var servers = await registry.ListAsync(context.NormalizedWorkspacePath, cancellationToken).ConfigureAwait(false);
            var ready = servers.Count(s => s.Status.State == McpLifecycleState.Ready);
            var faulted = servers.Count(s => s.Status.State == McpLifecycleState.Faulted);
            var details = new List<string> { $"{servers.Count} registered, {ready} ready, {faulted} faulted." };

            if (serverHost is not null)
            {
                foreach (var server in servers.OrderBy(s => s.Definition.Id, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var live = await serverHost.GetStatusAsync(context.NormalizedWorkspacePath, server.Definition.Id, cancellationToken).ConfigureAwait(false);
                        if (live is not null)
                        {
                            details.Add($"{server.Definition.Id}: host={live.State}");
                        }
                    }
                    catch (Exception exception)
                    {
                        details.Add($"{server.Definition.Id}: host probe failed ({exception.Message})");
                    }
                }
            }

            var status = faulted > 0 ? OperationalCheckStatus.Warn : OperationalCheckStatus.Ok;
            return new OperationalCheckItem(
                Id,
                status,
                "MCP registry looks healthy.",
                string.Join(" ", details));
        }
        catch (Exception exception)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Error,
                "MCP registry could not be read.",
                exception.Message);
        }
    }
}
