using System.CommandLine;
using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.UnitTests.Commands;

/// <summary>
/// Verifies command surfaces parse and report the spec primary mode.
/// </summary>
public sealed class ModeAndCliOptionsTests
{
    [Fact]
    public void Global_cli_options_should_parse_spec_primary_mode()
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All)
        {
            command.Options.Add(option);
        }

        var parseResult = command.Parse("--primary-mode spec");
        var context = options.Resolve(parseResult);

        context.PrimaryMode.Should().Be(PrimaryMode.Spec);
    }

    [Fact]
    public async Task Mode_slash_command_should_set_spec_mode()
    {
        var replState = new ReplInteractionState();
        var renderer = new StubOutputRenderer();
        var handler = new ModeSlashCommandHandler(replState, new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext(
            WorkingDirectory: "/workspace",
            Model: null,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            PrimaryMode: PrimaryMode.Build,
            SessionId: null);

        var exitCode = await handler.ExecuteAsync(
            new SlashCommandParseResult(true, "mode", ["spec"]),
            context,
            CancellationToken.None);

        exitCode.Should().Be(0);
        replState.PrimaryModeOverride.Should().Be(PrimaryMode.Spec);
        renderer.LastCommandResult!.Message.Should().Contain("Primary mode set to Spec");
    }

    private sealed class StubOutputRenderer : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Text;

        public CommandResult? LastCommandResult { get; private set; }

        public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
        {
            LastCommandResult = result;
            return Task.CompletedTask;
        }

        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
