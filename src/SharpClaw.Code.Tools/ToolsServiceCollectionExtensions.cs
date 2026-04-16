using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Permissions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Memory;
using SharpClaw.Code.Plugins;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Registry;
using SharpClaw.Code.Tools.Services;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Web;

namespace SharpClaw.Code.Tools;

/// <summary>
/// Registers the SharpClaw tool system and built-in tools.
/// </summary>
public static class ToolsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw tool system with configuration binding.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawTools(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddSharpClawTelemetry(configuration);
        services.AddSharpClawInfrastructure();
        services.AddSharpClawMemory();
        services.AddSharpClawPermissions();
        services.AddSharpClawPlugins();
        services.AddSharpClawWeb(configuration);
        return AddSharpClawToolsCore(services);
    }

    /// <summary>
    /// Adds the SharpClaw tool system to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawTools(this IServiceCollection services)
    {
        services.AddSharpClawTelemetry();
        services.AddSharpClawInfrastructure();
        services.AddSharpClawMemory();
        services.AddSharpClawPermissions();
        services.AddSharpClawPlugins();
        services.AddSharpClawWeb();
        return AddSharpClawToolsCore(services);
    }

    private static IServiceCollection AddSharpClawToolsCore(IServiceCollection services)
    {
        services.AddSingleton<ReadFileTool>();
        services.AddSingleton<WriteFileTool>();
        services.AddSingleton<EditFileTool>();
        services.AddSingleton<GlobSearchTool>();
        services.AddSingleton<GrepSearchTool>();
        services.AddSingleton<BashTool>();
        services.AddSingleton<WebSearchTool>();
        services.AddSingleton<WebFetchTool>();
        services.AddSingleton<WorkspaceSearchTool>();
        services.AddSingleton<SymbolSearchTool>();
        services.AddSingleton<ToolSearchTool>(serviceProvider =>
            new ToolSearchTool(() => serviceProvider.GetRequiredService<IToolRegistry>()));

        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<ReadFileTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<WriteFileTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<EditFileTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<GlobSearchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<GrepSearchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<BashTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<WebSearchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<WebFetchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<WorkspaceSearchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<SymbolSearchTool>());
        services.AddSingleton<ISharpClawTool>(serviceProvider => serviceProvider.GetRequiredService<ToolSearchTool>());

        services.AddSingleton<IToolRegistry>(serviceProvider => new ToolRegistry(
            serviceProvider.GetServices<ISharpClawTool>(),
            () => serviceProvider.GetRequiredService<IPluginManager>()));
        services.AddSingleton<IToolExecutor>(serviceProvider => new ToolExecutor(
            serviceProvider.GetRequiredService<IToolRegistry>(),
            serviceProvider.GetRequiredService<IPermissionPolicyEngine>(),
            serviceProvider.GetService<IRuntimeEventPublisher>()));
        services.AddSingleton<IToolPackageService, ToolPackageService>();
        return services;
    }
}
