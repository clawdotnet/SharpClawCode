using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Summarizes installed/enabled plugins and fault state.
/// </summary>
public sealed class PluginRegistryHealthCheck(IPluginManager pluginManager) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "plugins.registry";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        try
        {
            var plugins = await pluginManager.ListAsync(context.NormalizedWorkspacePath, cancellationToken).ConfigureAwait(false);
            var enabled = plugins.Count(p => p.State == PluginLifecycleState.Enabled);
            var faulted = plugins.Count(p => p.State == PluginLifecycleState.Faulted);
            var status = faulted > 0 ? OperationalCheckStatus.Warn : OperationalCheckStatus.Ok;
            return new OperationalCheckItem(
                Id,
                status,
                "Plugin registry readable.",
                $"{plugins.Count} installed, {enabled} enabled, {faulted} faulted.");
        }
        catch (Exception exception)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Error,
                "Plugin registry could not be read.",
                exception.Message);
        }
    }
}
