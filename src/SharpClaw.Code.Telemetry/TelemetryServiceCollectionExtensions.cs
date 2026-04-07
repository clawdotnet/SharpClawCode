using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Telemetry.Export;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.Telemetry;

/// <summary>
/// Registers SharpClaw telemetry services (event publishing, usage aggregation, JSON export).
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds telemetry primitives. Safe to call multiple times; core services use try-add semantics.
    /// Register <see cref="IRuntimeEventPersistence" /> separately (Sessions bridge) when durability is required.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSharpClawTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<TelemetryOptions>();
        services.TryAddSingleton<IUsageTracker, UsageTracker>();
        services.TryAddSingleton<JsonTraceExporter>();
        services.TryAddSingleton<IRuntimeEventPublisher>(serviceProvider => new RuntimeEventPublisher(
            serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelemetryOptions>>(),
            serviceProvider.GetRequiredService<IUsageTracker>(),
            serviceProvider.GetService<Microsoft.Extensions.Logging.ILogger<RuntimeEventPublisher>>(),
            serviceProvider.GetService<IRuntimeEventPersistence>()));
        return services;
    }
}
