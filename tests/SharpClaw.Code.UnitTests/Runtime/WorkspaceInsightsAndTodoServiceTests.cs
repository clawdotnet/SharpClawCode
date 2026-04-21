using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Sessions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry.Services;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class WorkspaceInsightsAndTodoServiceTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-insights-{Guid.NewGuid():N}");
    private readonly LocalFileSystem fileSystem = new();
    private readonly PathService pathService = new();

    [Fact]
    public async Task Todo_service_and_workspace_insights_should_persist_and_report_expected_state()
    {
        Directory.CreateDirectory(workspaceRoot);
        var clock = new FixedClock(DateTimeOffset.Parse("2026-04-13T18:00:00Z"));
        var storagePathResolver = TestRuntimeStorageResolver.Create(workspaceRoot, pathService);
        var sessionStore = new FileSessionStore(fileSystem, storagePathResolver);
        var eventStore = new NdjsonEventStore(fileSystem, storagePathResolver);
        var attachmentStore = new FileWorkspaceSessionAttachmentStore(fileSystem, storagePathResolver);
        var usageTracker = new UsageTracker();
        var todoService = new TodoService(sessionStore, eventStore, fileSystem, pathService, storagePathResolver, clock);
        var insights = new WorkspaceInsightsService(sessionStore, eventStore, attachmentStore, usageTracker, pathService, todoService);

        var session = new ConversationSession(
            "session-1",
            "Usage session",
            SessionLifecycleState.Active,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Json,
            workspaceRoot,
            workspaceRoot,
            clock.UtcNow.AddMinutes(-5),
            clock.UtcNow.AddMinutes(-5),
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal));
        await sessionStore.SaveAsync(workspaceRoot, session, CancellationToken.None);
        await attachmentStore.SetAttachedSessionIdAsync(workspaceRoot, session.Id, CancellationToken.None);

        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new TurnStartedEvent("evt-1", session.Id, "turn-1", clock.UtcNow.AddMinutes(-4), CreateTurn(session.Id, "turn-1")),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new ProviderStartedEvent(
                "evt-2",
                session.Id,
                "turn-1",
                clock.UtcNow.AddMinutes(-3),
                "anthropic",
                "sonnet",
                new ProviderRequest(
                    "req-1",
                    session.Id,
                    "turn-1",
                    "anthropic",
                    "sonnet",
                    "prompt",
                    null,
                    OutputFormat.Json,
                    null,
                    null)),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new ToolCompletedEvent("evt-3", session.Id, "turn-1", clock.UtcNow.AddMinutes(-2), new ToolResult("req-tool", "read_file", true, OutputFormat.Json, "{}", null, 0, 10, null)),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new TurnCompletedEvent("evt-4", session.Id, "turn-1", clock.UtcNow.AddMinutes(-1), CreateTurn(session.Id, "turn-1"), true, "done"),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new UsageUpdatedEvent("evt-5", session.Id, "turn-1", clock.UtcNow, new UsageSnapshot(100, 25, 0, 125, 0.12m)),
            CancellationToken.None);

        _ = await todoService.AddAsync(workspaceRoot, TodoScope.Session, "Investigate provider latency", session.Id, "primary-coding-agent", CancellationToken.None);
        _ = await todoService.AddAsync(workspaceRoot, TodoScope.Workspace, "Document usage commands", session.Id, null, CancellationToken.None);

        var snapshot = await todoService.GetSnapshotAsync(workspaceRoot, session.Id, CancellationToken.None);
        var usage = await insights.BuildUsageReportAsync(workspaceRoot, session.Id, CancellationToken.None);
        var cost = await insights.BuildCostReportAsync(workspaceRoot, session.Id, CancellationToken.None);
        var stats = await insights.BuildStatsReportAsync(workspaceRoot, session.Id, CancellationToken.None);

        snapshot.SessionTodos.Should().ContainSingle(item => item.Title.Contains("provider latency", StringComparison.OrdinalIgnoreCase));
        snapshot.WorkspaceTodos.Should().ContainSingle(item => item.Title.Contains("usage commands", StringComparison.OrdinalIgnoreCase));
        usage.WorkspaceTotal.TotalTokens.Should().Be(125);
        cost.WorkspaceEstimatedCostUsd.Should().Be(0.12m);
        stats.TurnStartedCount.Should().Be(1);
        stats.TurnCompletedCount.Should().Be(1);
        stats.ToolExecutionCount.Should().Be(1);
        stats.ProviderRequestCount.Should().Be(1);
        stats.ActiveTodoCount.Should().Be(2);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, true);
        }
    }

    private static ConversationTurn CreateTurn(string sessionId, string turnId)
        => new(turnId, sessionId, 1, "prompt", "output", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "primary-coding-agent", null, null, null);

    private sealed class FixedClock(DateTimeOffset utcNow) : SharpClaw.Code.Infrastructure.Abstractions.ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
