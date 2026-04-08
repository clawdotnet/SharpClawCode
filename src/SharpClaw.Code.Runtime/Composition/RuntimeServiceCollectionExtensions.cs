using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Agents;
using SharpClaw.Code.Git;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory;
using SharpClaw.Code.Mcp;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Providers;
using SharpClaw.Code.Skills;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Context;
using SharpClaw.Code.Runtime.CustomCommands;
using SharpClaw.Code.Runtime.Bridge;
using SharpClaw.Code.Runtime.Diagnostics;
using SharpClaw.Code.Runtime.Diagnostics.Checks;
using SharpClaw.Code.Runtime.Export;
using SharpClaw.Code.Runtime.Lifecycle;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Prompts;
using SharpClaw.Code.Runtime.Sessions;
using SharpClaw.Code.Runtime.Specs;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Composition;

/// <summary>
/// Registers the minimal runtime services required for the initial scaffold.
/// </summary>
public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw runtime skeleton to the service collection using configuration-backed providers.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawRuntime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return AddSharpClawRuntimeCore(services, serviceCollection => serviceCollection.AddSharpClawProviders(configuration));
    }

    /// <summary>
    /// Adds the SharpClaw runtime skeleton to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawRuntime(this IServiceCollection services)
        => AddSharpClawRuntimeCore(services, serviceCollection => serviceCollection.AddSharpClawProviders());

    private static IServiceCollection AddSharpClawRuntimeCore(
        IServiceCollection services,
        Action<IServiceCollection> addProviders)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(addProviders);

        services.AddLogging();
        services.AddSharpClawTelemetry();
        services.AddSharpClawInfrastructure();
        addProviders(services);
        services.AddSharpClawMcp();
        services.AddSharpClawTools();
        services.AddSharpClawAgents();
        services.AddSharpClawMemory();
        services.AddSharpClawSkills();
        services.AddSharpClawGit();
        services.AddSingleton<ISessionStore, FileSessionStore>();
        services.AddSingleton<IEventStore, NdjsonEventStore>();
        services.AddSingleton<IRuntimeEventPersistence, EventStoreRuntimeEventPersistence>();
        services.AddSingleton<ICheckpointStore, FileCheckpointStore>();
        services.AddSingleton<IMutationSetStore, FileMutationSetStore>();
        services.AddSingleton<IWorkspaceSessionAttachmentStore, FileWorkspaceSessionAttachmentStore>();
        services.AddSingleton<IEditorContextBuffer, EditorContextBuffer>();
        services.AddSingleton<MutationWorkspaceApplier>();
        services.AddSingleton<CheckpointMutationCoordinator>();
        services.AddSingleton<ISessionCoordinator, SessionCoordinator>();
        services.AddSingleton<IPortableSessionBundleService, PortableSessionBundleService>();
        services.AddSingleton<ICustomCommandMarkdownParser, CustomCommandMarkdownParser>();
        services.AddSingleton<ICustomCommandDiscoveryService, CustomCommandDiscoveryService>();
        services.AddSingleton<IPromptReferenceResolver, PromptReferenceResolver>();
        services.AddSingleton<ISpecWorkflowService, SpecWorkflowService>();
        services.AddSingleton<ISessionExportService, SessionExportService>();
        services.AddSingleton<IPromptContextAssembler, PromptContextAssembler>();
        services.AddSingleton<ITurnRunner, DefaultTurnRunner>();
        services.AddSingleton<IRuntimeStateMachine, DefaultRuntimeStateMachine>();
        AddOperationalDiagnostics(services);
        services.AddSingleton<Orchestration.ConversationRuntime>();
        services.AddSingleton<IConversationRuntime>(serviceProvider => serviceProvider.GetRequiredService<Orchestration.ConversationRuntime>());
        services.AddSingleton<IRuntimeCommandService>(serviceProvider => serviceProvider.GetRequiredService<Orchestration.ConversationRuntime>());
        services.AddHostedService<Orchestration.RuntimeCoordinatorHostedServiceAdapter>();
        return services;
    }

    private static void AddOperationalDiagnostics(IServiceCollection services)
    {
        services.AddSingleton<IOperationalCheck, WorkspaceAccessibilityCheck>();
        services.AddSingleton<IOperationalCheck>(sp => new ConfigurationResolutionCheck(sp.GetService<IConfiguration>()));
        services.AddSingleton<IOperationalCheck, SessionStoreHealthCheck>();
        services.AddSingleton<IOperationalCheck, ShellAvailabilityCheck>();
        services.AddSingleton<IOperationalCheck, GitAvailabilityCheck>();
        services.AddSingleton<IOperationalCheck, ProviderAuthenticationCheck>();
        services.AddSingleton<IOperationalCheck>(sp => new McpRegistryHealthCheck(
            sp.GetRequiredService<IMcpRegistry>(),
            sp.GetService<IMcpServerHost>()));
        services.AddSingleton<IOperationalCheck, PluginRegistryHealthCheck>();
        services.AddSingleton<IOperationalDiagnosticsCoordinator>(sp => new OperationalDiagnosticsCoordinator(
            sp.GetServices<IOperationalCheck>(),
            sp.GetRequiredService<ISystemClock>(),
            sp.GetRequiredService<IPathService>(),
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<IMcpRegistry>(),
            sp.GetRequiredService<IPluginManager>(),
            sp.GetRequiredService<IEventStore>(),
            sp.GetService<IConfiguration>()));
    }
}
