using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SharpClaw.Code.Runtime;

/// <summary>
/// Exposes runtime registration from the root runtime namespace.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw runtime services to the service collection using configuration-backed providers.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawRuntime(this IServiceCollection services, IConfiguration configuration)
        => Composition.RuntimeServiceCollectionExtensions.AddSharpClawRuntime(services, configuration);

    /// <summary>
    /// Adds the SharpClaw runtime services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawRuntime(this IServiceCollection services)
        => Composition.RuntimeServiceCollectionExtensions.AddSharpClawRuntime(services);
}
