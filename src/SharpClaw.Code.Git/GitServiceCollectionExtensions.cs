using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Git.Services;
using SharpClaw.Code.Infrastructure;

namespace SharpClaw.Code.Git;

/// <summary>
/// Registers SharpClaw git workspace services.
/// </summary>
public static class GitServiceCollectionExtensions
{
    /// <summary>
    /// Adds SharpClaw git workspace services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawGit(this IServiceCollection services)
    {
        services.AddSharpClawInfrastructure();
        services.AddSingleton<IGitWorkspaceService, GitWorkspaceService>();
        return services;
    }
}
