using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.ParityHarness;

/// <summary>
/// Named parity scenarios validating runtime, tools, sessions, and local MCP surfaces.
/// </summary>
public sealed class ParityScenarioTests : IAsyncLifetime
{
    private string _workspace = null!;
    private ServiceProvider _provider = null!;

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-parity", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _provider = ParityTestHost.Create(replaceApprovals: null);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        _provider.Dispose();
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for CI agents with open handles.
        }
    }

    private ToolExecutionContext BaseToolContext(
        PermissionMode mode,
        bool interactive = false,
        PermissionRequestSourceKind source = PermissionRequestSourceKind.Runtime,
        string? sourceName = null,
        IReadOnlyCollection<string>? trustedPlugins = null)
        => new(
            SessionId: "parity-session",
            TurnId: "parity-turn",
            WorkspaceRoot: _workspace,
            WorkingDirectory: _workspace,
            PermissionMode: mode,
            OutputFormat: OutputFormat.Json,
            EnvironmentVariables: null,
            AllowedTools: null,
            AllowDangerousBypass: false,
            IsInteractive: interactive,
            SourceKind: source,
            SourceName: sourceName,
            TrustedPluginNames: trustedPlugins,
            TrustedMcpServerNames: null);

    [Fact]
    public async Task Streaming_text_assembles_provider_output()
    {
        using var provider = ParityTestHost.Create(replaceApprovals: null);
        var runtime = ParityTestHost.GetConversation(provider);
        var turn = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "parity streaming",
                SessionId: null,
                WorkingDirectory: _workspace,
                PermissionMode.WorkspaceWrite,
                OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamingText,
                }),
            CancellationToken.None);

        turn.FinalOutput.Should().Be("Hello world");
    }

    /// <summary>
    /// Mock <c>stream_failure</c> now fails the turn so recovery state can be validated explicitly.
    /// </summary>
    [Fact]
    public async Task Stream_failure_should_fail_the_turn()
    {
        using var provider = ParityTestHost.Create(replaceApprovals: null);
        var runtime = ParityTestHost.GetConversation(provider);
        var store = provider.GetRequiredService<ISessionStore>();
        var act = async () => await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "should not stream",
                SessionId: null,
                WorkingDirectory: _workspace,
                PermissionMode.WorkspaceWrite,
                OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamFailure,
                }),
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        var latestSession = await store.GetLatestAsync(_workspace, CancellationToken.None);
        latestSession.Should().NotBeNull();
        latestSession!.State.Should().Be(SessionLifecycleState.Failed);
    }

    [Fact]
    public async Task Read_file_roundtrip_returns_workspace_content()
    {
        var path = Path.Combine(_workspace, "fixture-read.txt");
        await File.WriteAllTextAsync(path, "parity-read");

        var tools = ParityTestHost.GetToolExecutor(_provider);
        var json = JsonSerializer.Serialize(new { path = "fixture-read.txt", offset = (int?)null, limit = (int?)null });
        var envelope = await tools.ExecuteAsync(
            ReadFileTool.ToolName,
            json,
            BaseToolContext(PermissionMode.ReadOnly),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
        envelope.Result.Output.Should().Contain("parity-read");
    }

    [Fact]
    public async Task Write_file_allowed_under_workspace_write_mode()
    {
        var tools = ParityTestHost.GetToolExecutor(_provider);
        var json = JsonSerializer.Serialize(new { path = "out.txt", content = "wrote" });
        var envelope = await tools.ExecuteAsync(
            WriteFileTool.ToolName,
            json,
            BaseToolContext(PermissionMode.WorkspaceWrite),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
        var full = Path.Combine(_workspace, "out.txt");
        (await File.ReadAllTextAsync(full)).Should().Be("wrote");
    }

    [Fact]
    public async Task Write_file_denied_in_read_only_mode()
    {
        var tools = ParityTestHost.GetToolExecutor(_provider);
        var json = JsonSerializer.Serialize(new { path = "blocked.txt", content = "no" });
        var envelope = await tools.ExecuteAsync(
            WriteFileTool.ToolName,
            json,
            BaseToolContext(PermissionMode.ReadOnly),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Grep_chunk_assembly_returns_multiple_matches()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "pkg"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "pkg", "a.txt"), "line needle one\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "pkg", "b.txt"), "line needle two\n");

        var tools = ParityTestHost.GetToolExecutor(_provider);
        var json = JsonSerializer.Serialize(new
        {
            pattern = "needle",
            glob = "pkg/*.txt",
            limit = 10,
            caseSensitive = true,
        });
        var envelope = await tools.ExecuteAsync(
            GrepSearchTool.ToolName,
            json,
            BaseToolContext(PermissionMode.ReadOnly),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
        envelope.Result.StructuredOutputJson.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize<GrepSearchToolResult>(envelope.Result.StructuredOutputJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        payload!.Matches.Length.Should().Be(2);
    }

    [Fact]
    public async Task Bash_stdout_roundtrip_echoes_output()
    {
        var tools = ParityTestHost.GetToolExecutor(_provider);
        var cmd = OperatingSystem.IsWindows() ? "echo parity_bash" : "echo parity_bash";
        var json = JsonSerializer.Serialize(new { command = cmd, workingDirectory = (string?)null, environmentVariables = (object?)null });
        var envelope = await tools.ExecuteAsync(
            BashTool.ToolName,
            json,
            BaseToolContext(PermissionMode.DangerFullAccess),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
        envelope.Result.Output.Should().Contain("parity_bash");
    }

    [Fact]
    public async Task Permission_prompt_approved_allows_shell_in_workspace_write()
    {
        using var provider = ParityTestHost.Create(replaceApprovals: true);
        var tools = ParityTestHost.GetToolExecutor(provider);
        var cmd = OperatingSystem.IsWindows() ? "echo ok" : "echo ok";
        var json = JsonSerializer.Serialize(new { command = cmd, workingDirectory = (string?)null, environmentVariables = (object?)null });
        var envelope = await tools.ExecuteAsync(
            BashTool.ToolName,
            json,
            BaseToolContext(PermissionMode.WorkspaceWrite, interactive: true),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Permission_prompt_denied_blocks_shell_in_workspace_write()
    {
        using var provider = ParityTestHost.Create(replaceApprovals: false);
        var tools = ParityTestHost.GetToolExecutor(provider);
        var cmd = OperatingSystem.IsWindows() ? "echo no" : "echo no";
        var json = JsonSerializer.Serialize(new { command = cmd, workingDirectory = (string?)null, environmentVariables = (object?)null });
        var envelope = await tools.ExecuteAsync(
            BashTool.ToolName,
            json,
            BaseToolContext(PermissionMode.WorkspaceWrite, interactive: true),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Plugin_tool_roundtrip_executes_fixture_plugin_echo()
    {
        var tools = ParityTestHost.GetToolExecutor(_provider);
        var json = JsonSerializer.Serialize(new { message = "parity-plugin" });
        var envelope = await tools.ExecuteAsync(
            ParityFixturePluginTool.ToolName,
            json,
            BaseToolContext(PermissionMode.WorkspaceWrite),
            CancellationToken.None);

        envelope.Result.Succeeded.Should().BeTrue();
        envelope.Result.Output.Should().Contain("parity-plugin");
    }

    [Fact]
    public async Task Mcp_partial_startup_marks_server_starting_without_ready()
    {
        var registry = _provider.GetRequiredService<IMcpRegistry>();
        var definition = new McpServerDefinition(
            Id: "parity-mcp",
            DisplayName: "Parity MCP",
            TransportKind: "stdio",
            Endpoint: "disabled-for-parity",
            EnabledByDefault: true,
            Environment: null,
            Arguments: null);

        await registry.RegisterAsync(_workspace, definition, CancellationToken.None);
        await registry.UpdateStatusAsync(
            _workspace,
            new McpServerStatus(
                ServerId: "parity-mcp",
                State: McpLifecycleState.Starting,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                StatusMessage: "partial startup",
                ToolCount: 0,
                IsHealthy: false,
                Pid: null,
                FailureKind: McpFailureKind.Startup,
                HandshakeSucceeded: false),
            CancellationToken.None);

        var list = await registry.ListAsync(_workspace, CancellationToken.None);
        list.Should().ContainSingle();
        list[0].Status.State.Should().Be(McpLifecycleState.Starting);
    }

    [Fact]
    public async Task Recovery_after_timeout_marks_session_failed()
    {
        using var provider = ParityTestHost.Create(replaceApprovals: null);
        var runtime = ParityTestHost.GetConversation(provider);
        var store = provider.GetRequiredService<ISessionStore>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var act = async () =>
        {
            await runtime.RunPromptAsync(
                new RunPromptRequest(
                    Prompt: "slow",
                    SessionId: null,
                    WorkingDirectory: _workspace,
                    PermissionMode.WorkspaceWrite,
                    OutputFormat.Text,
                    Metadata: new Dictionary<string, string>
                    {
                        [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamSlow,
                    }),
                cts.Token);
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        var session = await store.GetLatestAsync(_workspace, CancellationToken.None);
        session.Should().NotBeNull();
        session!.State.Should().Be(SessionLifecycleState.Failed);
    }

    /// <summary>
    /// Documents parity catalog entries for discoverability in test runners.
    /// </summary>
    [Fact]
    public void Scenario_catalog_contains_expected_keys()
    {
        ParityScenarioIds.All.Should().HaveCount(11);
        ParityScenarioIds.All.Should().OnlyHaveUniqueItems();
    }
}
