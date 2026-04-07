using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Services;
using SharpClaw.Code.Telemetry;

namespace SharpClaw.Code.Mcp;

/// <summary>
/// Registers SharpClaw MCP lifecycle services.
/// </summary>
public static class McpServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw MCP subsystem to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawMcp(this IServiceCollection services)
    {
        services.AddSharpClawTelemetry();
        services.AddSharpClawInfrastructure();
        services.AddSingleton<IMcpRegistry, FileBackedMcpRegistry>();
        services.AddSingleton<IMcpProcessSupervisor, SdkMcpProcessSupervisor>();
        services.AddSingleton<IMcpServerHost, ProcessMcpServerHost>();
        services.AddSingleton<IMcpDoctorService, McpDoctorService>();
        return services;
    }
}
