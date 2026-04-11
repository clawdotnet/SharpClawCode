using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Storage;

namespace SharpClaw.Code.UnitTests.Sessions;

/// <summary>
/// Verifies session store, event store, and checkpoint store against the real file system.
/// </summary>
public sealed class SessionStorageTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"sharpclaw-test-{Guid.NewGuid():N}");
    private readonly LocalFileSystem _fileSystem = new();
    private readonly PathService _pathService = new();

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private ConversationSession CreateSession(string id, DateTimeOffset updatedAt) =>
        new(
            Id: id,
            Title: $"Test {id}",
            State: SessionLifecycleState.Active,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            WorkingDirectory: _tempDir,
            RepositoryRoot: null,
            CreatedAtUtc: updatedAt,
            UpdatedAtUtc: updatedAt,
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: null);

    // ── FileSessionStore ──

    [Fact]
    public async Task FileSessionStore_save_and_get_roundtrip()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);
        var session = CreateSession("s1", DateTimeOffset.UtcNow);

        await store.SaveAsync(_tempDir, session, CancellationToken.None);
        var loaded = await store.GetByIdAsync(_tempDir, "s1", CancellationToken.None);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be("s1");
        loaded.Title.Should().Be("Test s1");
        loaded.State.Should().Be(SessionLifecycleState.Active);
    }

    [Fact]
    public async Task FileSessionStore_get_returns_null_when_missing()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);

        var loaded = await store.GetByIdAsync(_tempDir, "nonexistent", CancellationToken.None);

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task FileSessionStore_get_latest_returns_most_recently_updated()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);
        var older = CreateSession("s-old", DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = CreateSession("s-new", DateTimeOffset.UtcNow);

        await store.SaveAsync(_tempDir, older, CancellationToken.None);
        await store.SaveAsync(_tempDir, newer, CancellationToken.None);

        var latest = await store.GetLatestAsync(_tempDir, CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be("s-new");
    }

    [Fact]
    public async Task FileSessionStore_get_latest_returns_null_when_empty()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);

        var latest = await store.GetLatestAsync(_tempDir, CancellationToken.None);

        latest.Should().BeNull();
    }

    [Fact]
    public async Task FileSessionStore_list_all_returns_sessions_descending()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(_tempDir, CreateSession("s1", now.AddMinutes(-5)), CancellationToken.None);
        await store.SaveAsync(_tempDir, CreateSession("s2", now), CancellationToken.None);
        await store.SaveAsync(_tempDir, CreateSession("s3", now.AddMinutes(-10)), CancellationToken.None);

        var all = await store.ListAllAsync(_tempDir, CancellationToken.None);

        all.Should().HaveCount(3);
        all[0].Id.Should().Be("s2");
        all[2].Id.Should().Be("s3");
    }

    [Fact]
    public async Task FileSessionStore_save_overwrites_existing()
    {
        var store = new FileSessionStore(_fileSystem, _pathService);
        var session = CreateSession("s1", DateTimeOffset.UtcNow);
        await store.SaveAsync(_tempDir, session, CancellationToken.None);

        var updated = session with { Title = "Updated", UpdatedAtUtc = DateTimeOffset.UtcNow.AddSeconds(1) };
        await store.SaveAsync(_tempDir, updated, CancellationToken.None);

        var loaded = await store.GetByIdAsync(_tempDir, "s1", CancellationToken.None);
        loaded!.Title.Should().Be("Updated");
    }

    // ── NdjsonEventStore ──

    [Fact]
    public async Task NdjsonEventStore_append_and_read_roundtrip()
    {
        var store = new NdjsonEventStore(_fileSystem, _pathService);
        var evt = new UndoCompletedEvent(
            EventId: "e1",
            SessionId: "s1",
            TurnId: "t1",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            MutationSetId: "m1");

        // Ensure session directory exists for the event store.
        var sessionsRoot = Path.Combine(_tempDir, ".sharpclaw", "sessions", "s1");
        Directory.CreateDirectory(sessionsRoot);

        await store.AppendAsync(_tempDir, "s1", evt, CancellationToken.None);
        var events = await store.ReadAllAsync(_tempDir, "s1", CancellationToken.None);

        events.Should().ContainSingle();
        events[0].EventId.Should().Be("e1");
        events[0].SessionId.Should().Be("s1");
    }

    [Fact]
    public async Task NdjsonEventStore_read_returns_empty_when_no_file()
    {
        var store = new NdjsonEventStore(_fileSystem, _pathService);

        var events = await store.ReadAllAsync(_tempDir, "nonexistent", CancellationToken.None);

        events.Should().BeEmpty();
    }

    [Fact]
    public async Task NdjsonEventStore_appends_multiple_events()
    {
        var store = new NdjsonEventStore(_fileSystem, _pathService);
        var sessionsRoot = Path.Combine(_tempDir, ".sharpclaw", "sessions", "s1");
        Directory.CreateDirectory(sessionsRoot);

        for (var i = 0; i < 5; i++)
        {
            await store.AppendAsync(_tempDir, "s1", new UndoCompletedEvent(
                EventId: $"e{i}",
                SessionId: "s1",
                TurnId: $"t{i}",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                MutationSetId: $"m{i}"), CancellationToken.None);
        }

        var events = await store.ReadAllAsync(_tempDir, "s1", CancellationToken.None);

        events.Should().HaveCount(5);
        events[0].EventId.Should().Be("e0");
        events[4].EventId.Should().Be("e4");
    }

    [Fact]
    public async Task NdjsonEventStore_skips_malformed_lines()
    {
        var sessionsRoot = Path.Combine(_tempDir, ".sharpclaw", "sessions", "s1");
        Directory.CreateDirectory(sessionsRoot);
        var eventsPath = Path.Combine(sessionsRoot, "events.ndjson");

        // Write a valid event followed by garbage.
        var store = new NdjsonEventStore(_fileSystem, _pathService);
        await store.AppendAsync(_tempDir, "s1", new UndoCompletedEvent(
            EventId: "e1",
            SessionId: "s1",
            TurnId: "t1",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            MutationSetId: "m1"), CancellationToken.None);

        await File.AppendAllTextAsync(eventsPath, "not-valid-json\n");

        var events = await store.ReadAllAsync(_tempDir, "s1", CancellationToken.None);

        events.Should().ContainSingle();
        events[0].EventId.Should().Be("e1");
    }

    // ── FileCheckpointStore ──

    [Fact]
    public async Task FileCheckpointStore_save_and_get_latest_roundtrip()
    {
        var store = new FileCheckpointStore(_fileSystem, _pathService);
        var now = DateTimeOffset.UtcNow;
        var older = new RuntimeCheckpoint("cp-old", "s1", "t1", now.AddMinutes(-5), "checkpoint old", "state-old", null, null);
        var newer = new RuntimeCheckpoint("cp-new", "s1", "t2", now, "checkpoint new", "state-new", null, null);

        var checkpointsRoot = Path.Combine(_tempDir, ".sharpclaw", "sessions", "s1", "checkpoints");
        Directory.CreateDirectory(checkpointsRoot);

        await store.SaveAsync(_tempDir, older, CancellationToken.None);
        await store.SaveAsync(_tempDir, newer, CancellationToken.None);

        var latest = await store.GetLatestAsync(_tempDir, "s1", CancellationToken.None);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be("cp-new");
    }

    [Fact]
    public async Task FileCheckpointStore_returns_null_when_no_checkpoints()
    {
        var store = new FileCheckpointStore(_fileSystem, _pathService);

        var latest = await store.GetLatestAsync(_tempDir, "nonexistent", CancellationToken.None);

        latest.Should().BeNull();
    }
}
