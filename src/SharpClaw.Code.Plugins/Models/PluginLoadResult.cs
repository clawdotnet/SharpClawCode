using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Plugins.Models;

/// <summary>
/// Captures the outcome of loading a plugin through the configured loader.
/// </summary>
/// <param name="Succeeded">Indicates whether the plugin loaded successfully.</param>
/// <param name="LoaderKind">The loader strategy used for the attempt.</param>
/// <param name="FailureReason">The failure reason, if loading failed.</param>
/// <param name="FailureKind">The typed failure category, if any.</param>
public sealed record PluginLoadResult(
    bool Succeeded,
    string LoaderKind,
    string? FailureReason,
    PluginFailureKind FailureKind = PluginFailureKind.None);
