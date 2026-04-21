using System.CommandLine;
using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

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
    public void Global_cli_options_should_parse_embedded_host_context()
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All)
        {
            command.Options.Add(option);
        }

        var storageRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-state");
        var parseResult = command.Parse($"--tenant-id tenant-a --host-id host-a --storage-root \"{storageRoot}\" --session-store sqlite");
        var context = options.Resolve(parseResult);

        context.HostContext.Should().BeEquivalentTo(new RuntimeHostContext(
            HostId: "host-a",
            TenantId: "tenant-a",
            StorageRoot: Path.GetFullPath(storageRoot),
            SessionStoreKind: SessionStoreKind.Sqlite,
            IsEmbeddedHost: true));
    }

    [Fact]
    public void Global_cli_options_should_force_danger_full_access_when_yolo_is_set()
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All)
        {
            command.Options.Add(option);
        }

        var parseResult = command.Parse("-y");
        var context = options.Resolve(parseResult);

        context.PermissionMode.Should().Be(PermissionMode.DangerFullAccess);
    }

    [Fact]
    public void Global_cli_options_should_parse_auto_approve_settings_and_budget()
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All)
        {
            command.Options.Add(option);
        }

        var parseResult = command.Parse("--auto-approve shell,network --auto-approve-budget 4");
        var context = options.Resolve(parseResult);

        context.ApprovalSettings.Should().BeEquivalentTo(new ApprovalSettings(
            [ApprovalScope.ShellExecution, ApprovalScope.NetworkAccess],
            4));
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

    [Fact]
    public async Task Approvals_slash_command_should_set_auto_approve_override()
    {
        var replState = new ReplInteractionState();
        var renderer = new StubOutputRenderer();
        var handler = new ApprovalsSlashCommandHandler(replState, new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext(
            WorkingDirectory: "/workspace",
            Model: null,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            PrimaryMode: PrimaryMode.Build,
            SessionId: null);

        var exitCode = await handler.ExecuteAsync(
            new SlashCommandParseResult(true, "approvals", ["set", "shell,promptRead", "2"]),
            context,
            CancellationToken.None);

        exitCode.Should().Be(0);
        replState.ApprovalSettingsOverride.Should().BeEquivalentTo(new ApprovalSettings(
            [ApprovalScope.ShellExecution, ApprovalScope.PromptOutsideWorkspaceRead],
            2));
        renderer.LastCommandResult!.Message.Should().Contain("Auto-approval override set");
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
