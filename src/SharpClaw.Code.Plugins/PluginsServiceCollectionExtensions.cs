using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Services;
using SharpClaw.Code.Telemetry;

namespace SharpClaw.Code.Plugins;

/// <summary>
/// Registers SharpClaw plugin management services.
/// </summary>
public static class PluginsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw plugin subsystem to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawPlugins(this IServiceCollection services)
    {
        services.AddSharpClawTelemetry();
        services.AddSharpClawInfrastructure();
        services.AddSingleton<PluginManifestValidator>();
        services.AddSingleton<IPluginProcessRunner, PluginProcessRunner>();
        services.AddSingleton<IPluginLoader, OutOfProcessPluginLoader>();
        services.AddSingleton<IPluginManager, PluginManager>();
        return services;
    }
}
