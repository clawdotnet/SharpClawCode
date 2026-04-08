using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;

namespace SharpClaw.Code.IntegrationTests.Smoke;

/// <summary>
/// Verifies the first CLI command surface and supporting services.
/// </summary>
public sealed class CliCommandSurfaceTests
{
    /// <summary>
    /// Ensures the host registers the command registry and REPL host abstractions.
    /// </summary>
    [Fact]
    public void BuildHost_should_register_command_registry_and_repl_host()
    {
        using var host = CliHostBuilder.BuildHost();

        host.Services.GetRequiredService<ICommandRegistry>().Should().NotBeNull();
        host.Services.GetRequiredService<IReplHost>().Should().NotBeNull();
    }

    /// <summary>
    /// Ensures the root command exposes the expected top-level commands and global options.
    /// </summary>
    [Fact]
    public async Task Root_command_should_expose_expected_commands_and_global_options()
    {
        using var host = CliHostBuilder.BuildHost();
        var commandFactory = host.Services.GetRequiredService<CliCommandFactory>();
        var rootCommand = await commandFactory.CreateRootCommandAsync();

        rootCommand.Subcommands.Select(command => command.Name).Should().Contain(
            [
                "acp",
                "bridge",
                "commands",
                "mcp",
                "plugins",
                "prompt",
                "status",
                "doctor",
                "version",
                "repl"
            ]);

        rootCommand.Options.Select(option => option.Name).Should().Contain(
            [
                "--output-format",
                "--cwd",
                "--model",
                "--permission-mode",
                "--primary-mode",
                "--session"
            ]);
    }
}
