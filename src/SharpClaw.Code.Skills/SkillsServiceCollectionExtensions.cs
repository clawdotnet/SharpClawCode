using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Skills.Abstractions;
using SharpClaw.Code.Skills.Services;

namespace SharpClaw.Code.Skills;

/// <summary>
/// Registers SharpClaw local skill services.
/// </summary>
public static class SkillsServiceCollectionExtensions
{
    /// <summary>
    /// Adds SharpClaw local skill services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawSkills(this IServiceCollection services)
    {
        services.AddSharpClawInfrastructure();
        services.AddSingleton<ISkillRegistry, SkillRegistry>();
        return services;
    }
}
