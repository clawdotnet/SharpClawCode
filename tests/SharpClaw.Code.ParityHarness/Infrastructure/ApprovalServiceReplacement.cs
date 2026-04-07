using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Permissions.Abstractions;

namespace SharpClaw.Code.ParityHarness.Infrastructure;

/// <summary>
/// Swaps the composite <see cref="IApprovalService"/> registration for a <see cref="ScriptedApprovalService"/>.
/// </summary>
internal static class ApprovalServiceReplacement
{
    /// <summary>
    /// Removes existing <see cref="IApprovalService"/> registrations and adds a scripted implementation.
    /// </summary>
    public static IServiceCollection ReplaceWithScriptedApprovals(this IServiceCollection services, bool approve)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == typeof(IApprovalService))
            {
                services.RemoveAt(i);
            }
        }

        services.AddSingleton<IApprovalService>(_ => new ScriptedApprovalService(approve));
        return services;
    }
}
