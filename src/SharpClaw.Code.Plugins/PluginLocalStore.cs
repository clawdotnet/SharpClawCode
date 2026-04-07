namespace SharpClaw.Code.Plugins;

/// <summary>
/// Describes on-disk layout for workspace-scoped plugin metadata under the workspace root.
/// </summary>
/// <remarks>
/// Each plugin lives in <c>{workspace}/.sharpclaw/plugins/{pluginId}/</c> with a JSON manifest and persisted lifecycle snapshot.
/// Trust and tool declarations are read from the manifest and flow into permission evaluation (manifest trust tier and per-tool approval flags).
/// </remarks>
public static class PluginLocalStore
{
    /// <summary>The workspace-relative directory name for all SharpClaw state.</summary>
    public const string SharpClawRelativeDirectoryName = ".sharpclaw";

    /// <summary>The directory under <see cref="SharpClawRelativeDirectoryName" /> that holds plugin folders.</summary>
    public const string PluginsRelativeDirectoryName = "plugins";

    /// <summary>The manifest filename inside each plugin directory.</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>The persisted <see cref="SharpClaw.Code.Protocol.Models.LoadedPlugin" /> snapshot filename.</summary>
    public const string StateFileName = "state.json";

    /// <summary>Optional opaque package or distribution reference written at install time.</summary>
    public const string PackageContentFileName = "package.txt";
}
