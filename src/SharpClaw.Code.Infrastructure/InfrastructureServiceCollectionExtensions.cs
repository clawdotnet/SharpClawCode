using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Abstractions;

namespace SharpClaw.Code.Infrastructure;

/// <summary>
/// Registers reusable infrastructure services.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default infrastructure service implementations.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IFileSystem, LocalFileSystem>();
        services.AddSingleton<ISystemClock, SystemClock>();
        services.AddSingleton<IPathService, PathService>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IShellExecutor, ShellExecutor>();
        services.AddSingleton<IUserProfilePaths, UserProfilePaths>();
        services.AddSingleton<IRuntimeHostContextAccessor, RuntimeHostContextAccessor>();
        services.AddSingleton<IRuntimeStoragePathResolver, RuntimeStoragePathResolver>();
        services.AddSingleton<IExternalEditorService, ExternalEditorService>();
        return services;
    }
}
