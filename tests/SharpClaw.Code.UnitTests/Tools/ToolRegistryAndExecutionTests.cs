using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Permissions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Permissions.Rules;
using SharpClaw.Code.Permissions.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Registry;

namespace SharpClaw.Code.UnitTests.Tools;

/// <summary>
/// Verifies the initial built-in tool registry and execution flow.
/// </summary>
public sealed class ToolRegistryAndExecutionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ensures built-in tools are discoverable through the registry and DI registration.
    /// </summary>
    [Fact]
    public async Task AddSharpClawTools_should_register_built_in_tools_and_registry()
    {
        var services = new ServiceCollection();
        services.AddSharpClawInfrastructure();
        services.AddSharpClawPermissions();
        services.AddSharpClawTools();
        using var serviceProvider = services.BuildServiceProvider();

        var registry = serviceProvider.GetRequiredService<IToolRegistry>();
        var definitions = (await registry.ListAsync(cancellationToken: CancellationToken.None))
            .Select(definition => definition.Name)
            .ToArray();

        definitions.Should().Contain([
            ReadFileTool.ToolName,
            WriteFileTool.ToolName,
            EditFileTool.ToolName,
            GlobSearchTool.ToolName,
            GrepSearchTool.ToolName,
            BashTool.ToolName,
            ToolSearchTool.ToolName
        ]);
    }

    /// <summary>
    /// Ensures file and search tools operate within the workspace boundary through the executor.
    /// </summary>
    [Fact]
    public async Task ToolExecutor_should_execute_file_and_search_tools_within_workspace()
    {
        var workspacePath = CreateTemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspacePath, "src"));
        var registry = CreateRegistryWithStubShell();
        var executor = CreateExecutor(registry);
        var context = CreateContext(workspacePath, PermissionMode.DangerFullAccess);

        var writeResult = await executor.ExecuteAsync(
            WriteFileTool.ToolName,
            JsonSerializer.Serialize(new WriteFileToolArguments("src/example.txt", "hello world")),
            context,
            CancellationToken.None);

        writeResult.Result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(workspacePath, "src", "example.txt")).Should().Be("hello world");

        var readResult = await executor.ExecuteAsync(
            ReadFileTool.ToolName,
            JsonSerializer.Serialize(new ReadFileToolArguments("src/example.txt", null, null)),
            context,
            CancellationToken.None);

        readResult.Result.Succeeded.Should().BeTrue();
        readResult.Result.Output.Should().Contain("hello world");

        var editResult = await executor.ExecuteAsync(
            EditFileTool.ToolName,
            JsonSerializer.Serialize(new EditFileToolArguments("src/example.txt", "world", "sharpclaw")),
            context,
            CancellationToken.None);

        editResult.Result.Succeeded.Should().BeTrue();
        File.ReadAllText(Path.Combine(workspacePath, "src", "example.txt")).Should().Be("hello sharpclaw");

        await executor.ExecuteAsync(
            WriteFileTool.ToolName,
            JsonSerializer.Serialize(new WriteFileToolArguments("docs/readme.md", "needle")),
            context,
            CancellationToken.None);

        var globResult = await executor.ExecuteAsync(
            GlobSearchTool.ToolName,
            JsonSerializer.Serialize(new GlobSearchToolArguments("*.txt", 20)),
            context,
            CancellationToken.None);

        globResult.Result.Succeeded.Should().BeTrue();
        var globPayload = JsonSerializer.Deserialize<GlobSearchToolResult>(globResult.Result.StructuredOutputJson!, JsonOptions);
        globPayload!.Paths.Should().Contain("src/example.txt");

        var grepResult = await executor.ExecuteAsync(
            GrepSearchTool.ToolName,
            JsonSerializer.Serialize(new GrepSearchToolArguments("needle", "*.*", 20, false)),
            context,
            CancellationToken.None);

        grepResult.Result.Succeeded.Should().BeTrue();
        var grepPayload = JsonSerializer.Deserialize<GrepSearchToolResult>(grepResult.Result.StructuredOutputJson!, JsonOptions);
        grepPayload!.Matches.Should().ContainSingle(match => match.Path == "docs/readme.md");
    }

    /// <summary>
    /// Ensures the executor rejects attempts to access files outside the workspace.
    /// </summary>
    [Fact]
    public async Task ToolExecutor_should_reject_paths_outside_workspace()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var registry = CreateRegistryWithStubShell();
        var executor = CreateExecutor(registry);
        var context = CreateContext(workspacePath, PermissionMode.DangerFullAccess);

        var result = await executor.ExecuteAsync(
            WriteFileTool.ToolName,
            JsonSerializer.Serialize(new WriteFileToolArguments("../escape.txt", "nope")),
            context,
            CancellationToken.None);

        result.Result.Succeeded.Should().BeFalse();
        result.Result.ErrorMessage.Should().Contain("workspace");
        File.Exists(Path.Combine(Path.GetDirectoryName(workspacePath)!, "escape.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Ensures symlinked paths that resolve outside the workspace are denied.
    /// </summary>
    [Fact]
    public async Task ToolExecutor_should_reject_symlink_escape_within_workspace()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var outsideRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-tool-tests-outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var linkPath = Path.Combine(workspacePath, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkPath, outsideRoot);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        var registry = CreateRegistryWithStubShell();
        var executor = CreateExecutor(registry);
        var context = CreateContext(workspacePath, PermissionMode.DangerFullAccess);

        var result = await executor.ExecuteAsync(
            WriteFileTool.ToolName,
            JsonSerializer.Serialize(new WriteFileToolArguments("linked/escape.txt", "nope")),
            context,
            CancellationToken.None);

        result.Result.Succeeded.Should().BeFalse();
        result.Result.ErrorMessage.Should().Contain("workspace");
        File.Exists(Path.Combine(outsideRoot, "escape.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Ensures the permission layer blocks destructive tools in read-only mode.
    /// </summary>
    [Fact]
    public async Task ToolExecutor_should_block_destructive_tools_in_read_only_mode()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var registry = CreateRegistryWithStubShell();
        var executor = CreateExecutor(registry);
        var context = CreateContext(workspacePath, PermissionMode.ReadOnly);

        var result = await executor.ExecuteAsync(
            WriteFileTool.ToolName,
            JsonSerializer.Serialize(new WriteFileToolArguments("notes.txt", "blocked")),
            context,
            CancellationToken.None);

        result.PermissionDecision.IsAllowed.Should().BeFalse();
        result.Result.Succeeded.Should().BeFalse();
        File.Exists(Path.Combine(workspacePath, "notes.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Ensures the bash tool delegates to the shell abstraction.
    /// </summary>
    [Fact]
    public async Task ToolExecutor_should_run_bash_tool_through_shell_executor()
    {
        var shellExecutor = new StubShellExecutor();
        var registry = CreateRegistry(shellExecutor, null);
        var executor = CreateExecutor(registry);
        var workspacePath = CreateTemporaryWorkspace();
        var context = CreateContext(workspacePath, PermissionMode.DangerFullAccess);

        var result = await executor.ExecuteAsync(
            BashTool.ToolName,
            JsonSerializer.Serialize(new BashToolArguments("echo hello", ".", null)),
            context,
            CancellationToken.None);

        shellExecutor.Commands.Should().ContainSingle();
        shellExecutor.Commands[0].Command.Should().Be("echo hello");
        var pathService = new PathService();
        shellExecutor.Commands[0].WorkingDirectory.Should().Be(pathService.GetCanonicalFullPath(workspacePath));
        result.Result.Succeeded.Should().BeTrue();
        result.Result.Output.Should().Contain("hello");
    }

    /// <summary>
    /// Ensures the tool search tool returns discoverable metadata from the registry.
    /// </summary>
    [Fact]
    public async Task ToolSearchTool_should_return_matching_tool_metadata()
    {
        var registry = CreateRegistryWithStubShell();
        var executor = CreateExecutor(registry);
        var context = CreateContext(CreateTemporaryWorkspace(), PermissionMode.DangerFullAccess);

        var result = await executor.ExecuteAsync(
            ToolSearchTool.ToolName,
            JsonSerializer.Serialize(new ToolSearchToolArguments("file", 10)),
            context,
            CancellationToken.None);

        result.Result.Succeeded.Should().BeTrue();
        var payload = JsonSerializer.Deserialize<ToolSearchToolResult>(result.Result.StructuredOutputJson!, JsonOptions);
        payload!.Tools.Select(tool => tool.Name).Should().Contain(ReadFileTool.ToolName);
        payload.Tools.Select(tool => tool.Name).Should().Contain(WriteFileTool.ToolName);
    }

    /// <summary>
    /// Ensures enabled plugin tools are surfaced into the registry through the controlled plugin path.
    /// </summary>
    [Fact]
    public async Task ToolRegistry_should_include_enabled_plugin_tools()
    {
        var registry = CreateRegistry(new StubShellExecutor(), new StubPluginManager());

        var names = (await registry.ListAsync(cancellationToken: CancellationToken.None))
            .Select(definition => definition.Name);
        names.Should().Contain("plugin_echo");
    }

    /// <summary>
    /// Ensures plugin tool resolution uses the supplied workspace root instead of only the process current directory.
    /// </summary>
    [Fact]
    public async Task ToolRegistry_should_pass_workspace_root_to_plugin_manager_when_listing()
    {
        var pluginManager = new StubPluginManager();
        var registry = CreateRegistry(new StubShellExecutor(), pluginManager);
        var workspace = CreateTemporaryWorkspace();

        _ = (await registry.ListAsync(workspace, CancellationToken.None)).ToArray();

        pluginManager.LastWorkspacePassedToListTools.Should().Be(workspace);
    }

    private static IToolRegistry CreateRegistryWithStubShell()
        => CreateRegistry(new StubShellExecutor(), null);

    private static IToolRegistry CreateRegistry(IShellExecutor shellExecutor, IPluginManager? pluginManager)
    {
        var fileSystem = new LocalFileSystem();
        var pathService = new PathService();
        ToolRegistry? registry = null;
        var tools = new ISharpClawTool[]
        {
            new ReadFileTool(fileSystem, pathService),
            new WriteFileTool(fileSystem, pathService),
            new EditFileTool(fileSystem, pathService),
            new GlobSearchTool(pathService),
            new GrepSearchTool(pathService),
            new BashTool(shellExecutor, pathService),
            new ToolSearchTool(() => registry ?? throw new InvalidOperationException("Registry not initialized."))
        };

        registry = new ToolRegistry(tools, () => pluginManager);
        return registry;
    }

    private static IToolExecutor CreateExecutor(IToolRegistry registry)
        => new ToolExecutor(
            registry,
            new PermissionPolicyEngine(
                [
                    new WorkspaceBoundaryRule(new PathService()),
                    new PrimaryModeMutationRule(),
                    new AllowedToolRule(),
                    new DangerousShellPatternRule(),
                    new PluginTrustRule(),
                    new McpTrustRule()
                ],
                new NonInteractiveApprovalService(),
                new SessionApprovalMemory()));

    private static ToolExecutionContext CreateContext(string workspacePath, PermissionMode permissionMode)
        => new(
            SessionId: "session-001",
            TurnId: "turn-001",
            WorkspaceRoot: workspacePath,
            WorkingDirectory: workspacePath,
            PermissionMode: permissionMode,
            OutputFormat: OutputFormat.Text,
            EnvironmentVariables: null,
            AllowedTools: null,
            AllowDangerousBypass: false,
            IsInteractive: false,
            SourceKind: PermissionRequestSourceKind.Runtime,
            SourceName: null,
            TrustedPluginNames: null,
            TrustedMcpServerNames: null);

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private sealed class StubShellExecutor : IShellExecutor
    {
        public List<(string Command, string? WorkingDirectory)> Commands { get; } = [];

        public Task<ProcessRunResult> ExecuteAsync(
            string command,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            CancellationToken cancellationToken)
        {
            Commands.Add((command, workingDirectory));
            return Task.FromResult(new ProcessRunResult(0, "hello\n", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubPluginManager : IPluginManager
    {
        public string? LastWorkspacePassedToListTools { get; private set; }

        public Task<IReadOnlyList<LoadedPlugin>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LoadedPlugin>>([]);

        public Task<LoadedPlugin> InstallAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> EnableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> DisableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UninstallAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> UpdateAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PluginToolDescriptor>> ListToolDescriptorsAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            LastWorkspacePassedToListTools = workspaceRoot;
            return Task.FromResult<IReadOnlyList<PluginToolDescriptor>>([
                new PluginToolDescriptor("plugin_echo", "Echo through plugin.", "JSON payload to echo.", ["plugin"])
            ]);
        }

        public Task<ToolResult> ExecuteToolAsync(string workspaceRoot, string toolName, ToolExecutionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult(request.Id, toolName, true, OutputFormat.Text, "plugin", null, 0, null, null));
    }
}
