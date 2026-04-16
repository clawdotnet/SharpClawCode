using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Permissions.Abstractions;

namespace SharpClaw.Code.Acp;

/// <summary>
/// Registers ACP-specific host services.
/// </summary>
public static class AcpServiceCollectionExtensions
{
    /// <summary>
    /// Adds ACP host services, including approval round-trip support.
    /// </summary>
    public static IServiceCollection AddSharpClawAcp(this IServiceCollection services)
    {
        services.AddSingleton<AcpApprovalCoordinator>();
        services.AddSingleton<IApprovalTransport, AcpApprovalTransport>();
        services.AddSingleton<AcpStdioHost>();
        return services;
    }
}
