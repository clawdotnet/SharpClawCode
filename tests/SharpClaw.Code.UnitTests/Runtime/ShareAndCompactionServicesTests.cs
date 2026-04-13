using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class ShareAndCompactionServicesTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-runtime-{Guid.NewGuid():N}");
    private readonly LocalFileSystem fileSystem = new();
    private readonly PathService pathService = new();

    [Fact]
    public async Task Share_and_compaction_services_persist_expected_session_metadata()
    {
        Directory.CreateDirectory(workspaceRoot);

        var clock = new FixedClock(DateTimeOffset.Parse("2026-04-13T15:00:00Z"));
        var sessionStore = new FileSessionStore(fileSystem, pathService);
        var eventStore = new NdjsonEventStore(fileSystem, pathService);
        var session = new ConversationSession(
            Id: "session-1",
            Title: "Initial title",
            State: SessionLifecycleState.Active,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Json,
            WorkingDirectory: workspaceRoot,
            RepositoryRoot: workspaceRoot,
            CreatedAtUtc: clock.UtcNow.AddMinutes(-10),
            UpdatedAtUtc: clock.UtcNow.AddMinutes(-5),
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal));
        await sessionStore.SaveAsync(workspaceRoot, session, CancellationToken.None);

        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new TurnStartedEvent(
                "event-1",
                session.Id,
                "turn-1",
                clock.UtcNow.AddMinutes(-4),
                new ConversationTurn(
                    "turn-1",
                    session.Id,
                    1,
                    "Add diagnostics support for the server",
                    null,
                    clock.UtcNow.AddMinutes(-4),
                    null,
                    "primary-coding-agent",
                    null,
                    null,
                    null)),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new TurnCompletedEvent(
                "event-2",
                session.Id,
                "turn-1",
                clock.UtcNow.AddMinutes(-3),
                new ConversationTurn(
                    "turn-1",
                    session.Id,
                    1,
                    "Add diagnostics support for the server",
                    "Implemented diagnostics.",
                    clock.UtcNow.AddMinutes(-4),
                    clock.UtcNow.AddMinutes(-3),
                    "primary-coding-agent",
                    null,
                    null,
                    null),
                true,
                "Implemented diagnostics."),
            CancellationToken.None);
        await eventStore.AppendAsync(
            workspaceRoot,
            session.Id,
            new ToolCompletedEvent(
                "event-3",
                session.Id,
                "turn-1",
                clock.UtcNow.AddMinutes(-2),
                new ToolResult("tool-1", "read_file", true, OutputFormat.Json, "{}", null, 0, 15, null)),
            CancellationToken.None);

        var publisher = new RuntimeEventPublisher(
            Options.Create(new TelemetryOptions()),
            new UsageTracker(),
            persistence: new EventStoreRuntimeEventPersistence(eventStore));
        var hooks = new RecordingHookDispatcher();
        var shareService = new ShareSessionService(
            fileSystem,
            pathService,
            clock,
            sessionStore,
            eventStore,
            new FixedConfigService(workspaceRoot),
            publisher,
            hooks);
        var compactionService = new ConversationCompactionService(sessionStore, eventStore, clock);

        var share = await shareService.CreateShareAsync(workspaceRoot, session.Id, CancellationToken.None);
        var sharedSession = await sessionStore.GetByIdAsync(workspaceRoot, session.Id, CancellationToken.None);
        var compacted = await compactionService.CompactAsync(workspaceRoot, session.Id, CancellationToken.None);
        var removed = await shareService.RemoveShareAsync(workspaceRoot, session.Id, CancellationToken.None);
        var unsharedSession = await sessionStore.GetByIdAsync(workspaceRoot, session.Id, CancellationToken.None);

        share.Url.Should().Be("http://127.0.0.1:7345/s/" + share.ShareId);
        fileSystem.FileExists(SessionStorageLayout.GetShareSnapshotPath(pathService, workspaceRoot, share.ShareId)).Should().BeFalse();
        sharedSession!.Metadata.Should().ContainKey(SharpClawWorkflowMetadataKeys.ShareId);
        compacted.Session.Metadata.Should().ContainKey(SharpClawWorkflowMetadataKeys.CompactedSummary);
        compacted.Summary.Should().Contain("Recent requests:");
        compacted.Session.Title.Should().Contain("Add diagnostics support");
        removed.Should().BeTrue();
        unsharedSession!.Metadata.Should().NotContainKey(SharpClawWorkflowMetadataKeys.ShareId);
        hooks.Invocations.Should().Contain(invocation => invocation.Trigger == HookTriggerKind.ShareCreated);
        hooks.Invocations.Should().Contain(invocation => invocation.Trigger == HookTriggerKind.ShareRemoved);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FixedConfigService(string workspaceRoot) : ISharpClawConfigService
    {
        public Task<SharpClawConfigSnapshot> GetConfigAsync(string _, CancellationToken cancellationToken)
            => Task.FromResult(
                new SharpClawConfigSnapshot(
                    workspaceRoot,
                    null,
                    null,
                    new SharpClawConfigDocument(
                        ShareMode.Manual,
                        new SharpClawServerOptions("127.0.0.1", 7345, null),
                        null,
                        null,
                        null,
                        null,
                        null)));
    }

    private sealed class RecordingHookDispatcher : IHookDispatcher
    {
        public List<(string WorkspaceRoot, HookTriggerKind Trigger, string PayloadJson)> Invocations { get; } = [];

        public Task DispatchAsync(string workspaceRoot, HookTriggerKind trigger, string payloadJson, CancellationToken cancellationToken)
        {
            Invocations.Add((workspaceRoot, trigger, payloadJson));
            return Task.CompletedTask;
        }
    }
}
