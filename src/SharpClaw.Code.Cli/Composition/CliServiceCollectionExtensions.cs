using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Options;

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
        services.AddSingleton<GlobalCliOptions>();
        services.AddSingleton<ReplInteractionState>();
        services.AddSingleton<CliCommandFactory>();
        services.AddSingleton<SlashCommandParser>();
        services.AddSingleton<OutputRendererDispatcher>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<IReplHost, ReplHost>();
        services.AddSingleton<IReplTerminal, Terminal.SpectreReplTerminal>();
        services.AddSingleton<ReplCommandHandler>();
        services.AddSingleton<SessionCommandHandler>();
        services.AddSingleton<AcpStdioHost>();

        services.AddSingleton<ICommandHandler, PromptCommandHandler>();
        services.AddSingleton<ICommandHandler, StatusCommandHandler>();
        services.AddSingleton<ICommandHandler, DoctorCommandHandler>();
        services.AddSingleton<ICommandHandler>(serviceProvider => serviceProvider.GetRequiredService<SessionCommandHandler>());
        services.AddSingleton<ICommandHandler, ModelsCommandHandler>();
        services.AddSingleton<ICommandHandler, ConnectCommandHandler>();
        services.AddSingleton<ICommandHandler, AgentsCommandHandler>();
        services.AddSingleton<ICommandHandler, ShareCommandHandler>();
        services.AddSingleton<ICommandHandler, UnshareCommandHandler>();
        services.AddSingleton<ICommandHandler, CompactCommandHandler>();
        services.AddSingleton<ICommandHandler, ServeCommandHandler>();
        services.AddSingleton<ICommandHandler, CommandsCommandHandler>();
        services.AddSingleton<ICommandHandler, McpCommandHandler>();
        services.AddSingleton<ICommandHandler, PluginsCommandHandler>();
        services.AddSingleton<ICommandHandler, VersionCommandHandler>();
        services.AddSingleton<ICommandHandler, AcpCommandHandler>();
        services.AddSingleton<ICommandHandler, BridgeCommandHandler>();

        services.AddSingleton<ISlashCommandHandler, StatusCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, DoctorCommandHandler>();
        services.AddSingleton<ISlashCommandHandler>(serviceProvider => serviceProvider.GetRequiredService<SessionCommandHandler>());
        services.AddSingleton<ISlashCommandHandler, SessionsSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ModelsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ConnectCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, AgentsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ShareCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, UnshareCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CompactCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ServeCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, CommandsCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ModeSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, EditorSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, ExportSlashCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, VersionCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, UndoCommandHandler>();
        services.AddSingleton<ISlashCommandHandler, RedoCommandHandler>();

        services.AddSingleton<IOutputRenderer, Rendering.TextOutputRenderer>();
        services.AddSingleton<IOutputRenderer, Rendering.JsonOutputRenderer>();

        return services;
    }
}
