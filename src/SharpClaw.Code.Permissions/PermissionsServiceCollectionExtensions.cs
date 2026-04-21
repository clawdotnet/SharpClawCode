using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Rules;
using SharpClaw.Code.Permissions.Services;

namespace SharpClaw.Code.Permissions;

/// <summary>
/// Registers the default SharpClaw permission services.
/// </summary>
public static class PermissionsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw permission services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawPermissions(this IServiceCollection services)
    {
        services.AddSingleton<ISessionApprovalMemory, SessionApprovalMemory>();
        services.AddSingleton<IApprovalPrincipalAccessor, ApprovalPrincipalAccessor>();
        services.AddSingleton<ConsoleApprovalService>();
        services.AddSingleton<NonInteractiveApprovalService>();
        services.AddSingleton<IApprovalTransport, AuthenticatedApprovalTransport>();
        services.AddSingleton<IApprovalService, ApprovalService>();
        services.AddSingleton<IPermissionRule, WorkspaceBoundaryRule>();
        services.AddSingleton<IPermissionRule, PrimaryModeMutationRule>();
        services.AddSingleton<IPermissionRule, AllowedToolRule>();
        services.AddSingleton<IPermissionRule, DangerousShellPatternRule>();
        services.AddSingleton<IPermissionRule, PluginTrustRule>();
        services.AddSingleton<IPermissionRule, McpTrustRule>();
        services.AddSingleton<IPermissionPolicyEngine, PermissionPolicyEngine>();
        return services;
    }
}
