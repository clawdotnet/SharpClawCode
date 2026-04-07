using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Services;

/// <summary>
/// Default <see cref="IPluginLoader" />: runs plugin health checks and tool calls only through <see cref="IPluginProcessRunner" />
/// (out-of-process). No host-process assembly loading.
/// </summary>
public sealed class OutOfProcessPluginLoader(IPluginProcessRunner processRunner) : IPluginLoader
{
    /// <inheritdoc />
    public Task<PluginLoadResult> LoadAsync(PluginManifest manifest, CancellationToken cancellationToken)
        => processRunner.LoadAsync(manifest, cancellationToken);

    /// <inheritdoc />
    public Task<PluginExecutionResult> ExecuteToolAsync(
        PluginManifest manifest,
        PluginToolDescriptor tool,
        ToolExecutionRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken)
        => processRunner.ExecuteAsync(manifest, tool, request, workspaceRoot, cancellationToken);
}
