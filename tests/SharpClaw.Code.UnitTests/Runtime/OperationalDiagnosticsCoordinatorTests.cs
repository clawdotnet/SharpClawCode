using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Diagnostics;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Covers status-report quick-check selection.
/// </summary>
public sealed class OperationalDiagnosticsCoordinatorTests
{
    [Fact]
    public async Task BuildStatusReportAsync_should_include_local_runtime_health_in_quick_checks()
    {
        var coordinator = new OperationalDiagnosticsCoordinator(
            [
                new StubOperationalCheck("workspace.access"),
                new StubOperationalCheck("session.store"),
                new StubOperationalCheck("mcp.registry"),
                new StubOperationalCheck("plugins.registry"),
                new StubOperationalCheck("approval.auth"),
                new StubOperationalCheck("provider.local-runtimes", OperationalCheckStatus.Warn),
            ],
            new FixedClock(DateTimeOffset.Parse("2026-04-21T12:00:00Z")),
            new PathService(),
            new StubSessionStore(),
            new StubMcpRegistry(),
            new StubPluginManager(),
            new StubEventStore(),
            new StubWorkspaceDiagnosticsService());

        var report = await coordinator.BuildStatusReportAsync(
            new OperationalDiagnosticsInput("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json),
            CancellationToken.None);

        report.Checks.Should().Contain(check => check.Id == "provider.local-runtimes" && check.Status == OperationalCheckStatus.Warn);
    }

    private sealed class StubOperationalCheck(string id, OperationalCheckStatus status = OperationalCheckStatus.Ok) : IOperationalCheck
    {
        public string Id { get; } = id;

        public Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
            => Task.FromResult(new OperationalCheckItem(Id, status, "ok", "detail"));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class StubSessionStore : ISessionStore
    {
        public Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
            => Task.FromResult<ConversationSession?>(null);

        public Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
            => Task.FromResult<ConversationSession?>(null);

        public Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ConversationSession>>([]);

        public Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubMcpRegistry : IMcpRegistry
    {
        public Task<RegisteredMcpServer?> GetAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
            => Task.FromResult<RegisteredMcpServer?>(null);

        public Task<IReadOnlyList<RegisteredMcpServer>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RegisteredMcpServer>>([]);

        public Task<RegisteredMcpServer> RegisterAsync(string workspaceRoot, McpServerDefinition definition, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UpdateStatusAsync(string workspaceRoot, McpServerStatus status, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubPluginManager : IPluginManager
    {
        public Task<LoadedPlugin> DisableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> EnableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ToolResult> ExecuteToolAsync(string workspaceRoot, string toolName, ToolExecutionRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> InstallAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<LoadedPlugin>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LoadedPlugin>>([]);

        public Task<IReadOnlyList<PluginToolDescriptor>> ListToolDescriptorsAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PluginToolDescriptor>>([]);

        public Task UninstallAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<LoadedPlugin> UpdateAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubEventStore : IEventStore
    {
        public Task AppendAsync(string workspacePath, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<RuntimeEvent>> ReadAllAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RuntimeEvent>>([]);
    }

    private sealed class StubWorkspaceDiagnosticsService : IWorkspaceDiagnosticsService
    {
        public Task<WorkspaceDiagnosticsSnapshot> BuildSnapshotAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceDiagnosticsSnapshot(workspaceRoot, DateTimeOffset.UtcNow, [], []));
    }
}
