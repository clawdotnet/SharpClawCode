using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Mcp.Services;
using SharpClaw.Code.Plugins;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Plugins.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.McpPlugins;

/// <summary>
/// Verifies the first MCP and plugin lifecycle implementations.
/// </summary>
public sealed class McpAndPluginLifecycleTests
{
    /// <summary>
    /// Ensures MCP definitions can be registered and process lifecycle state is tracked explicitly.
    /// </summary>
    [Fact]
    public async Task Mcp_services_should_register_start_stop_and_report_typed_status()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var registry = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        var host = new ProcessMcpServerHost(registry, new StubMcpProcessSupervisor(), new FixedClock());

        await registry.RegisterAsync(
            workspacePath,
            new McpServerDefinition(
                Id: "filesystem",
                DisplayName: "Filesystem MCP",
                TransportKind: "stdio",
                Endpoint: "sharpclaw-mcp-filesystem",
                EnabledByDefault: true,
                Environment: null,
                Arguments: ["--serve"]),
            CancellationToken.None);

        var started = await host.StartAsync(workspacePath, "filesystem", CancellationToken.None);
        var listed = await registry.ListAsync(workspacePath, CancellationToken.None);
        var stopped = await host.StopAsync(workspacePath, "filesystem", CancellationToken.None);

        listed.Should().ContainSingle(server => server.Definition.Id == "filesystem");
        started.State.Should().Be(McpLifecycleState.Ready);
        started.Pid.Should().Be(4242);
        started.FailureKind.Should().Be(McpFailureKind.None);
        stopped.State.Should().Be(McpLifecycleState.Stopped);
    }

    /// <summary>
    /// Ensures startup and handshake failures are distinguished in MCP status reporting.
    /// </summary>
    [Fact]
    public async Task Mcp_host_should_distinguish_startup_and_handshake_failures()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var registry = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        await registry.RegisterAsync(
            workspacePath,
            new McpServerDefinition("broken", "Broken", "stdio", "broken-server", true, null, ["--serve"]),
            CancellationToken.None);

        var startupHost = new ProcessMcpServerHost(registry, new StubMcpProcessSupervisor(startFails: true), new FixedClock());
        var handshakeHost = new ProcessMcpServerHost(registry, new StubMcpProcessSupervisor(handshakeFails: true), new FixedClock());

        var startupFailed = await startupHost.StartAsync(workspacePath, "broken", CancellationToken.None);
        var handshakeFailed = await handshakeHost.StartAsync(workspacePath, "broken", CancellationToken.None);

        startupFailed.FailureKind.Should().Be(McpFailureKind.Startup);
        startupFailed.State.Should().Be(McpLifecycleState.Faulted);
        handshakeFailed.FailureKind.Should().Be(McpFailureKind.Handshake);
        handshakeFailed.HandshakeSucceeded.Should().BeFalse();
    }

    /// <summary>
    /// Ensures host status preserves explicit failure kinds returned by the supervisor.
    /// </summary>
    [Fact]
    public async Task Mcp_host_should_preserve_supervisor_failure_kind_overrides()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var registry = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        await registry.RegisterAsync(
            workspacePath,
            new McpServerDefinition("capabilities", "Capabilities", "stdio", "capabilities-server", true, null, ["--serve"]),
            CancellationToken.None);

        var host = new ProcessMcpServerHost(
            registry,
            new StubMcpProcessSupervisor(handshakeFails: true, failureKind: McpFailureKind.Capabilities),
            new FixedClock());

        var failed = await host.StartAsync(workspacePath, "capabilities", CancellationToken.None);

        failed.FailureKind.Should().Be(McpFailureKind.Capabilities);
        failed.State.Should().Be(McpLifecycleState.Faulted);
    }

    private static PluginManager CreatePluginManager()
        => new(
            new OutOfProcessPluginLoader(new StubPluginProcessRunner()),
            new PluginManifestValidator(),
            new LocalFileSystem(),
            new PathService(),
            TestRuntimeStorageResolver.Create(CreateTemporaryWorkspace()),
            new FixedClock());

    /// <summary>
    /// Ensures plugin manifests are validated and plugins can be installed, enabled, disabled, and surfaced as tools.
    /// </summary>
    [Fact]
    public async Task Plugin_manager_should_install_enable_disable_and_surface_tool_descriptors()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var manager = CreatePluginManager();

        var installed = await manager.InstallAsync(
            workspacePath,
            new PluginInstallRequest(
                Manifest: new PluginManifest(
                    Id: "acme.echo",
                    Name: "Acme Echo",
                    Version: "1.0.0",
                    Description: "Echo plugin.",
                    EntryPoint: "acme-echo",
                    Arguments: ["--plugin"],
                    Capabilities: ["tools"],
                    Tools:
                    [
                        new PluginToolDescriptor(
                            Name: "plugin_echo",
                            Description: "Echo through plugin.",
                            InputDescription: "JSON object with a message field.",
                            Tags: ["plugin", "echo"],
                            InputTypeName: "EchoPayload",
                            InputSchemaJson: """{"type":"object","properties":{"message":{"type":"string"}}}""")
                    ],
                    Trust: PluginTrustLevel.WorkspaceTrusted),
                PackageContent: null),
            CancellationToken.None);

        var enabled = await manager.EnableAsync(workspacePath, "acme.echo", CancellationToken.None);
        var listedTools = await manager.ListToolDescriptorsAsync(workspacePath, CancellationToken.None);
        var disabled = await manager.DisableAsync(workspacePath, "acme.echo", CancellationToken.None);

        installed.Descriptor.Id.Should().Be("acme.echo");
        enabled.State.Should().Be(PluginLifecycleState.Enabled);
        listedTools.Should().ContainSingle(tool => tool.Name == "plugin_echo");
        disabled.State.Should().Be(PluginLifecycleState.Disabled);
    }

    /// <summary>
    /// Ensures uninstall removes the on-disk plugin directory.
    /// </summary>
    [Fact]
    public async Task Plugin_manager_should_remove_directory_on_uninstall()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var manager = CreatePluginManager();
        await manager.InstallAsync(
            workspacePath,
            new PluginInstallRequest(
                Manifest: new PluginManifest(
                    "acme.rm",
                    "Remove me",
                    "1.0.0",
                    null,
                    "acme-rm",
                    null,
                    null,
                    null),
                PackageContent: null),
            CancellationToken.None);

        var pluginDir = Path.Combine(workspacePath, PluginLocalStore.SharpClawRelativeDirectoryName, PluginLocalStore.PluginsRelativeDirectoryName, "acme.rm");
        Directory.Exists(pluginDir).Should().BeTrue();

        await manager.UninstallAsync(workspacePath, "acme.rm", CancellationToken.None);

        Directory.Exists(pluginDir).Should().BeFalse();
    }

    /// <summary>
    /// Ensures updating an enabled plugin resets lifecycle so the host must re-enable after manifest replacement.
    /// </summary>
    [Fact]
    public async Task Plugin_manager_update_should_reset_enabled_state_to_discovered()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var manager = CreatePluginManager();
        var v1 = new PluginManifest(
            "acme.ver",
            "Versioned",
            "1.0.0",
            null,
            "acme-ver",
            null,
            null,
            null);
        await manager.InstallAsync(workspacePath, new PluginInstallRequest(v1, null), CancellationToken.None);
        var enabled = await manager.EnableAsync(workspacePath, "acme.ver", CancellationToken.None);
        enabled.State.Should().Be(PluginLifecycleState.Enabled);

        var v2 = v1 with { Version = "2.0.0" };
        var updated = await manager.UpdateAsync(workspacePath, new PluginInstallRequest(v2, null), CancellationToken.None);

        updated.State.Should().Be(PluginLifecycleState.Discovered);
        updated.Descriptor.Version.Should().Be("2.0.0");
    }

    /// <summary>
    /// Ensures conflicting tool names across enabled plugins are rejected at descriptor list time.
    /// </summary>
    [Fact]
    public async Task Plugin_manager_should_reject_duplicate_tool_names_across_enabled_plugins()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var manager = CreatePluginManager();
        var tool = new PluginToolDescriptor(
            "shared_tool",
            "T",
            "Payload",
            null);
        await manager.InstallAsync(
            workspacePath,
            new PluginInstallRequest(
                new PluginManifest("p.one", "One", "1", null, "e1", null, null, [tool]),
                null),
            CancellationToken.None);
        await manager.InstallAsync(
            workspacePath,
            new PluginInstallRequest(
                new PluginManifest("p.two", "Two", "1", null, "e2", null, null, [tool]),
                null),
            CancellationToken.None);
        await manager.EnableAsync(workspacePath, "p.one", CancellationToken.None);
        await manager.EnableAsync(workspacePath, "p.two", CancellationToken.None);

        var act = async () => await manager.ListToolDescriptorsAsync(workspacePath, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .Where(exception => exception.Message.Contains("shared_tool", StringComparison.Ordinal));
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-mcp-plugin-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    /// <summary>
    /// A session whose status cannot be saved and whose stop fails stays recorded, and the next start stops it first.
    /// </summary>
    [Fact]
    public async Task Mcp_host_should_keep_an_unstoppable_session_reachable_and_stop_it_before_restarting()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var inner = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        var registry = new FailingReadyStatusRegistry(inner);
        var supervisor = new TrackingSupervisor { FailNextStop = true };
        var host = new ProcessMcpServerHost(registry, supervisor, new FixedClock());
        await inner.RegisterAsync(
            workspacePath,
            new McpServerDefinition("leaky", "Leaky", "stdio", "leaky-mcp", true, null, []),
            CancellationToken.None);

        var firstStart = async () => await host.StartAsync(workspacePath, "leaky", CancellationToken.None);
        await firstStart.Should().ThrowAsync<IOException>();

        var recorded = (await inner.GetAsync(workspacePath, "leaky", CancellationToken.None))!.Status;
        recorded.State.Should().Be(McpLifecycleState.Faulted);
        recorded.SessionHandle.Should().Be(77, "the still-running session must stay reachable");

        registry.FailReady = false;
        var restarted = await host.StartAsync(workspacePath, "leaky", CancellationToken.None);

        restarted.State.Should().Be(McpLifecycleState.Ready);
        supervisor.StoppedHandles.Should().Contain(77, "the leftover session is stopped before a new one starts");
    }

    /// <summary>
    /// If the leftover session still cannot be stopped, a new start is refused and the recorded handle is kept.
    /// </summary>
    [Fact]
    public async Task Mcp_host_should_refuse_to_start_while_a_leftover_session_cannot_be_stopped()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var inner = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        var registry = new FailingReadyStatusRegistry(inner);
        var supervisor = new TrackingSupervisor { FailStops = 2 };
        var host = new ProcessMcpServerHost(registry, supervisor, new FixedClock());
        await inner.RegisterAsync(workspacePath, new McpServerDefinition("stuck", "Stuck", "stdio", "stuck-mcp", true, null, []), CancellationToken.None);

        await FluentActions.Awaiting(() => host.StartAsync(workspacePath, "stuck", CancellationToken.None)).Should().ThrowAsync<IOException>();
        registry.FailReady = false;
        await FluentActions.Awaiting(() => host.StartAsync(workspacePath, "stuck", CancellationToken.None)).Should().ThrowAsync<InvalidOperationException>();

        (await inner.GetAsync(workspacePath, "stuck", CancellationToken.None))!.Status.SessionHandle.Should().Be(77);
        supervisor.StartCount.Should().Be(1, "no second session may be started while the first is still running");
    }

    /// <summary>
    /// When neither the status nor the recovery record can be written, the session stays reachable in memory.
    /// </summary>
    [Fact]
    public async Task Mcp_host_should_keep_an_unrecordable_session_reachable_in_memory()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var inner = new FileBackedMcpRegistry(new LocalFileSystem(), new PathService(), new FixedClock());
        var registry = new FailingReadyStatusRegistry(inner) { FailFaulted = true };
        var supervisor = new TrackingSupervisor { FailStops = 1 };
        var host = new ProcessMcpServerHost(registry, supervisor, new FixedClock());
        await inner.RegisterAsync(workspacePath, new McpServerDefinition("ghost", "Ghost", "stdio", "ghost-mcp", true, null, []), CancellationToken.None);

        await FluentActions.Awaiting(() => host.StartAsync(workspacePath, "ghost", CancellationToken.None)).Should().ThrowAsync<IOException>();
        (await inner.GetAsync(workspacePath, "ghost", CancellationToken.None))!.Status.SessionHandle.Should().BeNull("nothing could be recorded");

        registry.FailReady = false;
        registry.FailFaulted = false;
        var restarted = await host.StartAsync(workspacePath, "ghost", CancellationToken.None);

        restarted.State.Should().Be(McpLifecycleState.Ready);
        supervisor.StoppedHandles.Should().Contain(77, "the in-memory orphan is stopped before the new start");
    }

    private sealed class FailingReadyStatusRegistry(IMcpRegistry inner) : IMcpRegistry
    {
        public bool FailReady { get; set; } = true;

        public bool FailFaulted { get; set; }

        public Task<RegisteredMcpServer> RegisterAsync(string workspaceRoot, McpServerDefinition definition, CancellationToken cancellationToken)
            => inner.RegisterAsync(workspaceRoot, definition, cancellationToken);

        public Task<IReadOnlyList<RegisteredMcpServer>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => inner.ListAsync(workspaceRoot, cancellationToken);

        public Task<RegisteredMcpServer?> GetAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
            => inner.GetAsync(workspaceRoot, serverId, cancellationToken);

        public Task UpdateStatusAsync(string workspaceRoot, McpServerStatus status, CancellationToken cancellationToken)
            => (FailReady && status.State == McpLifecycleState.Ready) || (FailFaulted && status.State == McpLifecycleState.Faulted)
                ? throw new IOException("registry unavailable")
                : inner.UpdateStatusAsync(workspaceRoot, status, cancellationToken);
    }

    private sealed class TrackingSupervisor : IMcpProcessSupervisor
    {
        private readonly HashSet<long> live = [];

        public bool FailNextStop { get => FailStops > 0; set => FailStops = value ? 1 : 0; }

        public int FailStops { get; set; }

        public int StartCount { get; private set; }

        public List<long> StoppedHandles { get; } = [];

        public Task<McpProcessStartResult> StartAsync(McpServerDefinition definition, string workingDirectory, CancellationToken cancellationToken)
        {
            StartCount++;
            var handle = StartCount == 1 ? 77 : 78;
            live.Add(handle);
            return Task.FromResult(new McpProcessStartResult(true, 4242, true, null, SessionHandle: handle));
        }

        public Task StopAsync(McpProcessStopRequest request, CancellationToken cancellationToken)
        {
            if (FailStops > 0)
            {
                FailStops--;
                throw new IOException("stop failed");
            }

            live.Remove(request.SessionHandle);
            StoppedHandles.Add(request.SessionHandle);
            return Task.CompletedTask;
        }

        public bool IsSessionActive(long sessionHandle) => live.Contains(sessionHandle);

        public bool IsSessionActive(long sessionHandle, string serverId) => live.Contains(sessionHandle);
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-04-06T00:00:00Z");
    }

    private sealed class StubMcpProcessSupervisor(
        bool startFails = false,
        bool handshakeFails = false,
        McpFailureKind? failureKind = null) : IMcpProcessSupervisor
    {
        public Task<McpProcessStartResult> StartAsync(McpServerDefinition definition, string workingDirectory, CancellationToken cancellationToken)
            => Task.FromResult(startFails
                ? new McpProcessStartResult(false, null, false, "startup failed")
                : handshakeFails
                    ? new McpProcessStartResult(true, 4242, false, "handshake failed", FailureKind: failureKind)
                    : new McpProcessStartResult(true, 4242, true, null));

        public Task StopAsync(McpProcessStopRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubPluginProcessRunner : IPluginProcessRunner
    {
        public Task<PluginLoadResult> LoadAsync(PluginManifest manifest, CancellationToken cancellationToken)
            => Task.FromResult(new PluginLoadResult(true, "out-of-process", null));

        public Task<PluginExecutionResult> ExecuteAsync(
            PluginManifest manifest,
            PluginToolDescriptor tool,
            ToolExecutionRequest request,
            string workspaceRoot,
            CancellationToken cancellationToken)
            => Task.FromResult(new PluginExecutionResult(true, "ok", null, 0, null));
    }
}
