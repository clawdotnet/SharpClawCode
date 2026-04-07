using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Cli;
using SharpClaw.Code.Commands;

namespace SharpClaw.Code.IntegrationTests.Smoke;

/// <summary>
/// Verifies slash command parsing for the initial REPL flow.
/// </summary>
public sealed class SlashCommandParserTests
{
    /// <summary>
    /// Ensures slash command input is parsed into a command name and arguments.
    /// </summary>
    [Fact]
    public void Parser_should_parse_slash_command_input()
    {
        using var host = CliHostBuilder.BuildHost();
        var parser = host.Services.GetRequiredService<SlashCommandParser>();

        var parsed = parser.Parse("/doctor --verbose");

        parsed.IsSlashCommand.Should().BeTrue();
        parsed.CommandName.Should().Be("doctor");
        parsed.Arguments.Should().Equal("--verbose");
    }

    /// <summary>
    /// Ensures normal prompt text is not treated as a slash command.
    /// </summary>
    [Fact]
    public void Parser_should_leave_normal_prompt_text_as_non_command()
    {
        using var host = CliHostBuilder.BuildHost();
        var parser = host.Services.GetRequiredService<SlashCommandParser>();

        var parsed = parser.Parse("inspect the repository");

        parsed.IsSlashCommand.Should().BeFalse();
        parsed.CommandName.Should().BeNull();
        parsed.Arguments.Should().BeEmpty();
    }
}
