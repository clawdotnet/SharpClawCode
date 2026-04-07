using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Services;

namespace SharpClaw.Code.Memory;

/// <summary>
/// Registers SharpClaw project memory services.
/// </summary>
public static class MemoryServiceCollectionExtensions
{
    /// <summary>
    /// Adds SharpClaw project memory services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawMemory(this IServiceCollection services)
    {
        services.AddSharpClawInfrastructure();
        services.AddSingleton<IProjectMemoryService, ProjectMemoryService>();
        services.AddSingleton<ISessionSummaryService, SessionSummaryService>();
        return services;
    }
}
