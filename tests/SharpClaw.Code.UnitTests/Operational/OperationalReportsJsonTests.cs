using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.UnitTests.Operational;

public sealed class OperationalReportsJsonTests
{
    [Fact]
    public void DoctorReport_round_trips_through_JsonContext()
    {
        var original = new DoctorReport(
            SchemaVersion: "1.0",
            GeneratedAtUtc: DateTimeOffset.Parse("2026-04-06T12:00:00Z"),
            OverallStatus: OperationalCheckStatus.Warn,
            WorkspaceRoot: "/tmp/ws",
            Checks:
            [
                new OperationalCheckItem("workspace.access", OperationalCheckStatus.Ok, "ok", "detail"),
            ],
            ConfigurationKeysSample: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SharpClaw:Test"] = "present",
            });

        var json = JsonSerializer.Serialize(original, ProtocolJsonContext.Default.DoctorReport);
        var restored = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.DoctorReport);
        restored.Should().NotBeNull();
        restored!.SchemaVersion.Should().Be("1.0");
        restored.OverallStatus.Should().Be(OperationalCheckStatus.Warn);
        restored.Checks.Should().HaveCount(1);
        restored.Checks[0].Id.Should().Be("workspace.access");
        restored.ConfigurationKeysSample!["SharpClaw:Test"].Should().Be("present");
    }

    [Fact]
    public void RuntimeStatusReport_round_trips_through_JsonContext()
    {
        var original = new RuntimeStatusReport(
            SchemaVersion: "1.0",
            GeneratedAtUtc: DateTimeOffset.Parse("2026-04-06T12:00:00Z"),
            WorkspaceRoot: "/tmp/ws",
            RuntimeState: "ready",
            SelectedModel: "deterministic",
            PermissionMode: PermissionMode.ReadOnly,
            PrimaryMode: PrimaryMode.Build,
            LatestSessionId: "sess-1",
            LatestSessionState: SessionLifecycleState.Active,
            McpServerCount: 1,
            McpReadyCount: 0,
            McpFaultedCount: 1,
            PluginInstalledCount: 2,
            PluginEnabledCount: 1,
            Checks: [new OperationalCheckItem("session.store", OperationalCheckStatus.Ok, "fine", null)]);

        var json = JsonSerializer.Serialize(original, ProtocolJsonContext.Default.RuntimeStatusReport);
        var restored = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.RuntimeStatusReport);
        restored.Should().NotBeNull();
        restored!.LatestSessionState.Should().Be(SessionLifecycleState.Active);
        restored.McpFaultedCount.Should().Be(1);
        restored.Checks.Should().HaveCount(1);
    }

    [Fact]
    public void SessionInspectionReport_round_trips_through_JsonContext()
    {
        var session = new ConversationSession(
            Id: "sess",
            Title: "t",
            State: SessionLifecycleState.Active,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Json,
            WorkingDirectory: "/w",
            RepositoryRoot: "/w",
            CreatedAtUtc: DateTimeOffset.Parse("2026-04-06T11:00:00Z"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-04-06T11:01:00Z"),
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: null);
        var original = new SessionInspectionReport("1.0", "/w", session, 3, "turnStarted, turnCompleted");

        var json = JsonSerializer.Serialize(original, ProtocolJsonContext.Default.SessionInspectionReport);
        var restored = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.SessionInspectionReport);
        restored.Should().NotBeNull();
        restored!.PersistedEventCount.Should().Be(3);
        restored.Session.Id.Should().Be("sess");
        restored.RecentEventSummary.Should().Contain("turnCompleted");
    }
}
