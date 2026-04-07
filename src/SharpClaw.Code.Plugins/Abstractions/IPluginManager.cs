using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Abstractions;

/// <summary>
/// Manages the local plugin catalog and plugin lifecycle state for a workspace.
/// </summary>
public interface IPluginManager
{
    /// <summary>
    /// Lists locally tracked plugins for the workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked plugins.</returns>
    Task<IReadOnlyList<LoadedPlugin>> ListAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Installs a plugin manifest into the local plugin store.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="request">The plugin to install.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked plugin snapshot.</returns>
    Task<LoadedPlugin> InstallAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Enables an installed plugin.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="pluginId">The plugin identifier to enable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated plugin snapshot.</returns>
    Task<LoadedPlugin> EnableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken);

    /// <summary>
    /// Disables an installed plugin.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="pluginId">The plugin identifier to disable.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated plugin snapshot.</returns>
    Task<LoadedPlugin> DisableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an installed plugin from the local store.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="pluginId">The plugin identifier to uninstall.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UninstallAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates an installed plugin by replacing its manifest.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="request">The updated plugin payload.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated plugin snapshot.</returns>
    Task<LoadedPlugin> UpdateAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Lists tool descriptors from enabled plugins.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The enabled plugin tool descriptors.</returns>
    Task<IReadOnlyList<PluginToolDescriptor>> ListToolDescriptorsAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Executes an enabled plugin tool.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local plugin store.</param>
    /// <param name="toolName">The plugin tool name to execute.</param>
    /// <param name="request">The tool execution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool result.</returns>
    Task<ToolResult> ExecuteToolAsync(string workspaceRoot, string toolName, ToolExecutionRequest request, CancellationToken cancellationToken);
}
