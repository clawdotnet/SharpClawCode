using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Telemetry.Export;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.Telemetry;

/// <summary>
/// Registers SharpClaw telemetry services (event publishing, usage aggregation, JSON export).
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    private const string TelemetrySectionName = "SharpClaw:Telemetry";

    /// <summary>
    /// Adds telemetry primitives with configuration binding. Safe to call multiple times; core services use try-add semantics.
    /// Register <see cref="IRuntimeEventPersistence" /> separately (Sessions bridge) when durability is required.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSharpClawTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<TelemetryOptions>()
            .Bind(configuration.GetSection(TelemetrySectionName));
        return AddSharpClawTelemetryCore(services);
    }

    /// <summary>
    /// Adds telemetry primitives. Safe to call multiple times; core services use try-add semantics.
    /// Register <see cref="IRuntimeEventPersistence" /> separately (Sessions bridge) when durability is required.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSharpClawTelemetry(this IServiceCollection services)
    {
        services.AddOptions<TelemetryOptions>();
        return AddSharpClawTelemetryCore(services);
    }

    private static IServiceCollection AddSharpClawTelemetryCore(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IValidateOptions<TelemetryOptions>, TelemetryOptionsValidator>();
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
