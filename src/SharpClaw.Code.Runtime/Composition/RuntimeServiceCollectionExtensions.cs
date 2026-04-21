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
using SharpClaw.Code.Runtime.Configuration;
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
using SharpClaw.Code.Runtime.Server;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Telemetry.Services;

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
        services.AddSharpClawTelemetry(configuration);
        return AddSharpClawRuntimeCore(services, serviceCollection => serviceCollection.AddSharpClawProviders(configuration), configuration);
    }

    /// <summary>
    /// Adds the SharpClaw runtime skeleton to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawRuntime(this IServiceCollection services)
        => AddSharpClawRuntimeCore(services, serviceCollection => serviceCollection.AddSharpClawProviders(), configuration: null);

    private static IServiceCollection AddSharpClawRuntimeCore(
        IServiceCollection services,
        Action<IServiceCollection> addProviders,
        IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(addProviders);

        services.AddLogging();
        if (configuration is not null)
        {
            services.AddSharpClawTelemetry(configuration);
        }
        else
        {
            services.AddSharpClawTelemetry();
        }

        services.AddSharpClawInfrastructure();
        addProviders(services);
        services.AddSharpClawMcp();
        if (configuration is not null)
        {
            services.AddSharpClawTools(configuration);
        }
        else
        {
            services.AddSharpClawTools();
        }

        services.AddSharpClawAgents();
        services.AddSharpClawMemory();
        services.AddSharpClawSkills();
        services.AddSharpClawGit();
        services.AddSingleton<IUsageMeteringStore, SqliteUsageMeteringStore>();
        services.AddSingleton<UsageMeteringService>();
        services.AddSingleton<IUsageMeteringService>(serviceProvider => serviceProvider.GetRequiredService<UsageMeteringService>());
        services.AddSingleton<IRuntimeEventSink>(serviceProvider => serviceProvider.GetRequiredService<UsageMeteringService>());
        services.AddSingleton<FileSessionStore>();
        services.AddSingleton<SqliteSessionStore>();
        services.AddSingleton<ISessionStore, HostAwareSessionStore>();
        services.AddSingleton<NdjsonEventStore>();
        services.AddSingleton<SqliteEventStore>();
        services.AddSingleton<IEventStore, HostAwareEventStore>();
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
        services.AddSingleton<ISharpClawConfigService, SharpClawConfigService>();
        services.AddSingleton<SharpClaw.Code.Permissions.Abstractions.IApprovalIdentityService, ConfiguredApprovalIdentityService>();
        services.AddSingleton<IAgentCatalogService, AgentCatalogService>();
        services.AddSingleton<IWorkspaceDiagnosticsService, WorkspaceDiagnosticsService>();
        services.AddSingleton<IShareSessionService, ShareSessionService>();
        services.AddSingleton<IConversationCompactionService, ConversationCompactionService>();
        services.AddSingleton<IHookDispatcher, HookDispatcher>();
        services.AddSingleton<ITodoService, TodoService>();
        services.AddSingleton<IWorkspaceInsightsService, WorkspaceInsightsService>();
        services.AddSingleton<IWorkspaceHttpServer, WorkspaceHttpServer>();
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
        services.AddSingleton<IOperationalCheck, ApprovalAuthCheck>();
        services.AddSingleton<IOperationalCheck, LocalRuntimeCatalogCheck>();
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
            sp.GetRequiredService<IWorkspaceDiagnosticsService>(),
            sp.GetService<IConfiguration>()));
    }
}
