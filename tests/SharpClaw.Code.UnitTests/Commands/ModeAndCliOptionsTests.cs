using System.CommandLine;
using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.UnitTests.Commands;

/// <summary>
/// Verifies command surfaces parse and report the spec primary mode.
/// </summary>
public sealed class ModeAndCliOptionsTests
{
    [Theory]
    [InlineData("--permission-mode bogus")]
    [InlineData("--primary-mode plna")]
    public void Invalid_modes_are_rejected(string arguments)
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All) command.Options.Add(option);
        FluentActions.Invoking(() => options.Resolve(command.Parse(arguments))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Unspecified_primary_mode_is_left_to_agent_or_session_defaults()
    {
        var options = new GlobalCliOptions();
        var command = new RootCommand();
        foreach (var option in options.All) command.Options.Add(option);
        options.Resolve(command.Parse("")).ToRuntimeCommandContext().PrimaryMode.Should().BeNull();
    }

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
        var sessionPreferences = new StubSessionPreferenceService();
        var handler = CreateApprovalsHandler(replState, renderer, sessionPreferences);
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
        sessionPreferences.LastApprovalSettings.Should().BeEquivalentTo(new ApprovalSettings(
            [ApprovalScope.ShellExecution, ApprovalScope.PromptOutsideWorkspaceRead],
            2));
        renderer.LastCommandResult!.Message.Should().Contain("Persisted session auto-approvals");
    }

    [Fact]
    public async Task Approvals_slash_command_reset_should_clear_override()
    {
        var replState = new ReplInteractionState
        {
            ApprovalSettingsOverride = new ApprovalSettings([ApprovalScope.ShellExecution], 1)
        };
        var renderer = new StubOutputRenderer();
        var sessionPreferences = new StubSessionPreferenceService();
        var handler = CreateApprovalsHandler(replState, renderer, sessionPreferences);
        var context = new CommandExecutionContext(
            WorkingDirectory: "/workspace",
            Model: null,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            PrimaryMode: PrimaryMode.Build,
            SessionId: null);

        var exitCode = await handler.ExecuteAsync(
            new SlashCommandParseResult(true, "approvals", ["reset"]),
            context,
            CancellationToken.None);

        exitCode.Should().Be(0);
        replState.ApprovalSettingsOverride.Should().BeNull();
        sessionPreferences.ClearApprovalSettingsCalled.Should().BeTrue();
        renderer.LastCommandResult!.Message.Should().Contain("Cleared durable session auto-approval settings");
    }

    private static ApprovalsSlashCommandHandler CreateApprovalsHandler(
        ReplInteractionState replState,
        IOutputRenderer renderer,
        ISessionPreferenceService sessionPreferences)
        => new(new PermissionsCommandHandler(sessionPreferences, replState, new OutputRendererDispatcher([renderer])));

    private sealed class StubSessionPreferenceService : ISessionPreferenceService
    {
        public ApprovalSettings? LastApprovalSettings { get; private set; }

        public bool ClearApprovalSettingsCalled { get; private set; }

        public Task<PermissionStatusReport> GetPermissionStatusAsync(
            string workspaceRoot,
            string? sessionId,
            PermissionMode fallbackPermissionMode,
            ApprovalSettings? approvalSettings,
            string? currentModel,
            CancellationToken cancellationToken)
            => Task.FromResult(new PermissionStatusReport(fallbackPermissionMode, approvalSettings, [], sessionId, currentModel));

        public Task<PermissionStatusReport> GrantTrustAsync(
            string workspaceRoot,
            string? sessionId,
            TrustedSourceKind kind,
            string name,
            PermissionMode fallbackPermissionMode,
            ApprovalSettings? approvalSettings,
            string? currentModel,
            CancellationToken cancellationToken)
            => GetPermissionStatusAsync(workspaceRoot, sessionId, fallbackPermissionMode, approvalSettings, currentModel, cancellationToken);

        public Task<PermissionStatusReport> RevokeTrustAsync(
            string workspaceRoot,
            string? sessionId,
            TrustedSourceKind kind,
            string name,
            PermissionMode fallbackPermissionMode,
            ApprovalSettings? approvalSettings,
            string? currentModel,
            CancellationToken cancellationToken)
            => GetPermissionStatusAsync(workspaceRoot, sessionId, fallbackPermissionMode, approvalSettings, currentModel, cancellationToken);

        public Task<SessionModelPreference> SetModelPreferenceAsync(string workspaceRoot, string? sessionId, string model, CancellationToken cancellationToken)
            => Task.FromResult(new SessionModelPreference(model, DateTimeOffset.UtcNow));

        public Task<bool> ClearModelPreferenceAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
            => Task.FromResult(true);

        public Task<PermissionMode> SetPreferredPermissionModeAsync(string workspaceRoot, string? sessionId, PermissionMode permissionMode, CancellationToken cancellationToken)
            => Task.FromResult(permissionMode);

        public Task<ApprovalSettings> SetApprovalSettingsAsync(string workspaceRoot, string? sessionId, ApprovalSettings approvalSettings, CancellationToken cancellationToken)
        {
            LastApprovalSettings = approvalSettings;
            return Task.FromResult(approvalSettings);
        }

        public Task<bool> ClearApprovalSettingsAsync(string workspaceRoot, string? sessionId, CancellationToken cancellationToken)
        {
            ClearApprovalSettingsCalled = true;
            return Task.FromResult(true);
        }
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
