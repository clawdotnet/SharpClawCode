using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Mcp.Server;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Testing.Cli;

namespace SharpClaw.Code.Cli.Composition;

/// <summary>
/// Registers the CLI command surface and terminal-facing services.
/// </summary>
public static class CliServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SharpClaw CLI vertical slice services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawCli(this IServiceCollection services)
    {
        services.AddSharpClawAcp();
        services.AddSharpClawMcpServer();
        services.AddSharpClawTestingCli();
        services.AddSingleton<GlobalCliOptions>();
        services.AddSingleton<ReplInteractionState>();
        services.AddSingleton<CliCommandFactory>();
        services.AddSingleton<SlashCommandParser>();
        services.AddSingleton<OutputRendererDispatcher>();
        services.AddSingleton<ICliInvocationEnvironment, Terminal.ConsoleInvocationEnvironment>();
        services.AddSingleton<PromptInvocationService>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<IReplHost, ReplHost>();
        services.AddSingleton<IReplTerminal, Terminal.SpectreReplTerminal>();
        services.AddSingleton<ReplCommandHandler>();
        services.AddSingleton<SessionCommandHandler>();
        services.AddSingleton<PermissionsCommandHandler>();
        services.AddSingleton<AuthCommandHandler>();
        services.AddSingleton<InitCommandHandler>();
        services.AddSingleton<ResearchCommandHandler>();
        services.AddSingleton<ScheduleCommandHandler>();
        services.AddSingleton<EvolutionCommandHandler>();
        services.AddSingleton<ModelsCommandHandler>();
        services.AddSingleton<AgentsCommandHandler>();
        services.AddSingleton<ICommandHandler, PromptCommandHandler>();
        services.AddSingleton<ICommandHandler, StatusCommandHandler>();
        services.AddSingleton<ICommandHandler, DoctorCommandHandler>();
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<SessionCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<PermissionsCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ModelsCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<AuthCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<InitCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ResearchCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ScheduleCommandHandler>());
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<EvolutionCommandHandler>());
        services.AddSingleton<ICommandHandler, UsageCommandHandler>();
        services.AddSingleton<ICommandHandler, CostCommandHandler>();
        services.AddSingleton<ICommandHandler, StatsCommandHandler>();
        services.AddSingleton<ICommandHandler, ConnectCommandHandler>();
        services.AddSingleton<ICommandHandler, IndexCommandHandler>();
        services.AddSingleton<ICommandHandler, VerifyCommandHandler>();
        services.AddSingleton<ICommandHandler, HooksCommandHandler>();
        services.AddSingleton<ICommandHandler, MemoryCommandHandler>();
        services.AddSingleton<ICommandHandler, SkillsCommandHandler>();
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<AgentsCommandHandler>());
        services.AddSingleton<ICommandHandler, ExternalCommandHandler>();
        services.AddSingleton<ICommandHandler, TodoCommandHandler>();
        services.AddSingleton<ICommandHandler, WorkCommandHandler>();
        services.AddSingleton<ICommandHandler, WorkbenchCommandHandler>();
        services.AddSingleton<ICommandHandler, ShareCommandHandler>();
        services.AddSingleton<ICommandHandler, UnshareCommandHandler>();
        services.AddSingleton<ICommandHandler, CompactCommandHandler>();
        services.AddSingleton<ICommandHandler, ServeCommandHandler>();
        services.AddSingleton<ICommandHandler, CommandsCommandHandler>();
        services.AddSingleton<ICommandHandler, WorktreeCommandHandler>();
        services.AddSingleton<ICommandHandler, McpCommandHandler>();
        services.AddSingleton<ICommandHandler, PluginsCommandHandler>();
        services.AddSingleton<ICommandHandler, ToolPackagesCommandHandler>();
        services.AddSingleton<ICommandHandler, VersionCommandHandler>();
        services.AddSingleton<ICommandHandler, AcpCommandHandler>();
        services.AddSingleton<ICommandHandler, BridgeCommandHandler>();

        services.AddSingleton<ISlashCommandHandler, StatusCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, DoctorCommandHandler>();
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<SessionCommandHandler>());
        services.AddSingleton<ISlashCommandHandler, SessionsSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<PermissionsCommandHandler>());
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ModelsCommandHandler>());
        services.AddSingleton<ISlashCommandHandler, ModelSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<AuthCommandHandler>());
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<InitCommandHandler>());
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ResearchCommandHandler>());
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<ScheduleCommandHandler>());
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<EvolutionCommandHandler>());
        services.AddSingleton<ISlashCommandHandler, UsageCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CostCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, StatsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ConnectCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, IndexCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, VerifyCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, HooksCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, MemoryCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, SkillsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<AgentsCommandHandler>());
        services.AddSingleton<ISlashCommandHandler, TodoCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, WorkCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, WorkbenchCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, AgentStatusSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CheckpointsSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ShareCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, UnshareCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CompactCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ServeCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CommandsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, WorktreeCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ModeSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ApprovalsSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, EditorSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ExportSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, VersionCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, UndoCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, RedoCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, NewSessionSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ResumeSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ClearSlashCommandHandler>();

        services.AddSingleton<IOutputRenderer, Rendering.TextOutputRenderer>();
        services.AddSingleton<IOutputRenderer, Rendering.JsonOutputRenderer>();

        return services;
    }
}
