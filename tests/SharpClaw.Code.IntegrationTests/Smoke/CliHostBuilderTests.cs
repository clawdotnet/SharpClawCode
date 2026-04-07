using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;

namespace SharpClaw.Code.IntegrationTests.Smoke;

/// <summary>
/// Verifies the CLI host bootstrap wiring.
/// </summary>
public sealed class CliHostBuilderTests
{
    /// <summary>
    /// Ensures the CLI host registers the command factory.
    /// </summary>
    [Fact]
    public async Task BuildHost_should_register_cli_command_factory()
    {
        using var host = CliHostBuilder.BuildHost();

        var commandFactory = host.Services.GetRequiredService<CliCommandFactory>();
        var rootCommand = await commandFactory.CreateRootCommandAsync();

        rootCommand.Description.Should().Be("SharpClaw Code CLI. Starts interactive mode when no command is supplied.");
    }
}
