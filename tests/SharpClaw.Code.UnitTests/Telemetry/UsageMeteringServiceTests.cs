using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Server;
using SharpClaw.Code.Telemetry.Services;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.Telemetry;

/// <summary>
/// Verifies event-driven usage metering persistence and filtering.
/// </summary>
public sealed class UsageMeteringServiceTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-metering", Guid.NewGuid().ToString("N"));
    private readonly string userRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-metering-user", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Usage_metering_should_aggregate_usage_and_filter_by_time_window()
    {
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(userRoot);

        var store = new SqliteUsageMeteringStore(
            new LocalFileSystem(),
            new PathService(),
            TestRuntimeStorageResolver.Create(userRoot));
        var metering = new UsageMeteringService(store);
        var startedAt = DateTimeOffset.Parse("2026-04-16T18:00:00Z");
        var turn = new ConversationTurn(
            Id: "turn-1",
            SessionId: "session-1",
            SequenceNumber: 1,
            Input: "Build the feature",
            Output: "Done.",
            StartedAtUtc: startedAt,
            CompletedAtUtc: startedAt.AddMilliseconds(120),
            AgentId: "primary",
            SlashCommandName: null,
            Usage: new UsageSnapshot(10, 6, 0, 16, 0.24m),
            Metadata: null);
        var providerRequest = new ProviderRequest(
            Id: "provider-1",
            SessionId: "session-1",
            TurnId: "turn-1",
            ProviderName: "mock",
            Model: "default",
            Prompt: "Build the feature",
            SystemPrompt: null,
            OutputFormat: OutputFormat.Text,
            Temperature: null,
            Metadata: null);

        await metering.PublishAsync(
            new RuntimeEventEnvelope(
                EventType: nameof(ProviderStartedEvent),
                OccurredAtUtc: startedAt,
                Event: new ProviderStartedEvent("evt-provider-start", "session-1", "turn-1", startedAt, "mock", "default", providerRequest),
                WorkspacePath: workspaceRoot,
                SessionId: "session-1",
                TenantId: "tenant-a",
                HostId: "host-a"),
            CancellationToken.None);
        await metering.PublishAsync(
            new RuntimeEventEnvelope(
                EventType: nameof(ProviderCompletedEvent),
                OccurredAtUtc: startedAt.AddMilliseconds(50),
                Event: new ProviderCompletedEvent(
                    "evt-provider-complete",
                    "session-1",
                    "turn-1",
                    startedAt.AddMilliseconds(50),
                    "mock",
                    "default",
                    "provider-terminal",
                    "completed",
                    new UsageSnapshot(10, 6, 0, 16, 0.24m)),
                WorkspacePath: workspaceRoot,
                SessionId: "session-1",
                TenantId: "tenant-a",
                HostId: "host-a"),
            CancellationToken.None);
        await metering.PublishAsync(
            new RuntimeEventEnvelope(
                EventType: nameof(ToolStartedEvent),
                OccurredAtUtc: startedAt.AddMilliseconds(60),
                Event: new ToolStartedEvent(
                    "evt-tool-start",
                    "session-1",
                    "turn-1",
                    startedAt.AddMilliseconds(60),
                    new ToolExecutionRequest(
                        "tool-1",
                        "session-1",
                        "turn-1",
                        "write_file",
                        """{"path":"notes.txt","content":"ok"}""",
                        ApprovalScope.FileSystemWrite,
                        workspaceRoot,
                        RequiresApproval: true,
                        IsDestructive: true)),
                WorkspacePath: workspaceRoot,
                SessionId: "session-1",
                TenantId: "tenant-a",
                HostId: "host-a"),
            CancellationToken.None);
        await metering.PublishAsync(
            new RuntimeEventEnvelope(
                EventType: nameof(ToolCompletedEvent),
                OccurredAtUtc: startedAt.AddMilliseconds(90),
                Event: new ToolCompletedEvent(
                    "evt-tool-complete",
                    "session-1",
                    "turn-1",
                    startedAt.AddMilliseconds(90),
                    new ToolResult("tool-1", "write_file", true, OutputFormat.Text, "ok", null, 0, null, null)),
                WorkspacePath: workspaceRoot,
                SessionId: "session-1",
                TenantId: "tenant-a",
                HostId: "host-a"),
            CancellationToken.None);
        await metering.PublishAsync(
            new RuntimeEventEnvelope(
                EventType: nameof(TurnCompletedEvent),
                OccurredAtUtc: startedAt.AddMilliseconds(120),
                Event: new TurnCompletedEvent("evt-turn-complete", "session-1", "turn-1", startedAt.AddMilliseconds(120), turn, true, "success"),
                WorkspacePath: workspaceRoot,
                SessionId: "session-1",
                TenantId: "tenant-a",
                HostId: "host-a"),
            CancellationToken.None);

        var summary = await metering.GetSummaryAsync(
            workspaceRoot,
            new UsageMeteringQuery(TenantId: "tenant-a", HostId: "host-a", WorkspaceRoot: workspaceRoot),
            CancellationToken.None);
        var detail = await metering.GetDetailAsync(
            workspaceRoot,
            new UsageMeteringQuery(WorkspaceRoot: workspaceRoot),
            20,
            CancellationToken.None);
        var filtered = await metering.GetSummaryAsync(
            workspaceRoot,
            new UsageMeteringQuery(FromUtc: startedAt.AddMilliseconds(55), WorkspaceRoot: workspaceRoot),
            CancellationToken.None);

        summary.TotalUsage.TotalTokens.Should().Be(16);
        summary.ProviderRequestCount.Should().Be(1);
        summary.ToolExecutionCount.Should().Be(1);
        summary.TurnCount.Should().Be(1);
        detail.Records.Should().Contain(record => record.Kind == UsageMeteringRecordKind.ProviderUsage && record.DurationMilliseconds == 50);
        detail.Records.Should().Contain(record =>
            record.Kind == UsageMeteringRecordKind.ToolExecution
            && record.DurationMilliseconds == 30
            && record.ApprovalScope == ApprovalScope.FileSystemWrite);
        filtered.TotalUsage.TotalTokens.Should().Be(0);
        filtered.ProviderRequestCount.Should().Be(0);
        filtered.ToolExecutionCount.Should().Be(1);
    }

    public void Dispose()
    {
        TestDirectoryCleanup.DeleteIfExists(workspaceRoot, clearSqlitePools: true);
        TestDirectoryCleanup.DeleteIfExists(userRoot, clearSqlitePools: true);
    }
}
