using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.ParityHarness.Infrastructure;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.ParityHarness;

/// <summary>
/// Builds a <see cref="ServiceProvider"/> that mirrors production registration while forcing the deterministic mock LLM.
/// </summary>
internal static class ParityTestHost
{
    /// <summary>
    /// Creates a root service provider with production-style registration and the deterministic mock LLM.
    /// </summary>
    /// <param name="replaceApprovals">When not null, swaps <c>IApprovalService</c> for scripted approve/deny behavior.</param>
    /// <param name="configure">Optional extra service registrations (runs last).</param>
    public static ServiceProvider Create(
        bool? replaceApprovals,
        Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSharpClawRuntime(configuration);
        services.AddDeterministicMockModelProvider();
        services.AddSingleton<ParityFixturePluginTool>();
        services.AddSingleton<ISharpClawTool>(static sp => sp.GetRequiredService<ParityFixturePluginTool>());

        if (replaceApprovals is { } decision)
        {
            services.ReplaceWithScriptedApprovals(decision);
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Resolves the conversation runtime as <see cref="IRuntimeCommandService"/> for parity scenarios.
    /// </summary>
    public static IRuntimeCommandService GetRuntime(ServiceProvider provider)
        => provider.GetRequiredService<IRuntimeCommandService>();

    /// <summary>
    /// Resolves the durable conversation runtime.
    /// </summary>
    public static IConversationRuntime GetConversation(ServiceProvider provider)
        => provider.GetRequiredService<IConversationRuntime>();

    /// <summary>
    /// Resolves the tool executor.
    /// </summary>
    public static IToolExecutor GetToolExecutor(ServiceProvider provider)
        => provider.GetRequiredService<IToolExecutor>();
}
