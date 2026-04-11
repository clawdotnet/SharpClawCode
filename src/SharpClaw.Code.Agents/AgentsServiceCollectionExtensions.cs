using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Agents.Configuration;
using SharpClaw.Code.Agents.Internal;
using SharpClaw.Code.Agents.Services;

namespace SharpClaw.Code.Agents;

/// <summary>
/// Registers the SharpClaw agent orchestration layer.
/// </summary>
public static class AgentsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw agents to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawAgents(this IServiceCollection services)
    {
        services.AddOptions<AgentLoopOptions>();
        services.AddSingleton<ToolCallDispatcher>();
        services.AddSingleton<ProviderBackedAgentKernel>();
        services.AddSingleton<IAgentFrameworkBridge, AgentFrameworkBridge>();
        services.AddSingleton<PrimaryCodingAgent>();
        services.AddSingleton<ReviewerAgent>();
        services.AddSingleton<AdvisorAgent>();
        services.AddSingleton<SecurityReviewAgent>();
        services.AddSingleton<SubAgentWorker>();
        services.AddSingleton<RecoveryAgent>();
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<PrimaryCodingAgent>());
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<ReviewerAgent>());
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<AdvisorAgent>());
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<SecurityReviewAgent>());
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<SubAgentWorker>());
        services.AddSingleton<ISharpClawAgent>(serviceProvider => serviceProvider.GetRequiredService<RecoveryAgent>());
        return services;
    }
}
