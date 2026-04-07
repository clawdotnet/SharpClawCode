using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a plugin that has been loaded or tracked by the runtime.
/// </summary>
/// <param name="Descriptor">The plugin descriptor.</param>
/// <param name="State">The current plugin lifecycle state.</param>
/// <param name="LoadedAtUtc">The UTC timestamp when the plugin was loaded or registered.</param>
/// <param name="FailureReason">The load or execution failure reason, if any.</param>
/// <param name="FailureKind">The typed failure category, if one is known.</param>
public sealed record LoadedPlugin(
    PluginDescriptor Descriptor,
    PluginLifecycleState State,
    DateTimeOffset LoadedAtUtc,
    string? FailureReason,
    PluginFailureKind FailureKind = PluginFailureKind.None);
