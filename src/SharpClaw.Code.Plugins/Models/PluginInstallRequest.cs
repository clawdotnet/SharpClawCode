namespace SharpClaw.Code.Plugins.Models;

/// <summary>
/// Describes a plugin manifest and optional package payload to install locally.
/// </summary>
/// <param name="Manifest">The plugin manifest to persist.</param>
/// <param name="PackageContent">Optional opaque package content retained for future installers.</param>
public sealed record PluginInstallRequest(
    PluginManifest Manifest,
    string? PackageContent);
