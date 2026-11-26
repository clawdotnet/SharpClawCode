using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Concurrency;
using SharpClaw.Code.Sessions.Storage;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.Sessions;

/// <summary>
/// Verifies out-of-turn session edits survive saves from stale session copies (e.g. a running turn).
/// </summary>
public sealed class ConcurrencySafeSessionStoreTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"sharpclaw-concurrency-{Guid.NewGuid():N}");
    private readonly string userRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-concurrency-user-{Guid.NewGuid():N}");
    private readonly LocalFileSystem fileSystem = new();
    private readonly ConcurrencySafeSessionStore store;
    private readonly SessionUpdateCoordinator coordinator;

    public ConcurrencySafeSessionStoreTests()
    {
        Directory.CreateDirectory(workspace);
        var resolver = TestRuntimeStorageResolver.Create(userRoot);
        store = new ConcurrencySafeSessionStore(new FileSessionStore(fileSystem, resolver), fileSystem, resolver);
        coordinator = new SessionUpdateCoordinator(fileSystem, resolver);
    }

    [Fact]
    public async Task Stale_save_carries_forward_an_out_of_turn_edit()
    {
        var turnCopy = await SeedAsync(new Dictionary<string, string> { ["trusted"] = "[\"plugin-a\"]" });

        await EditAsync(session => With(session, "trusted", "[]"));
        await store.SaveAsync(workspace, With(turnCopy, "lastTurnId", "turn-1"), CancellationToken.None);

        var persisted = await store.GetByIdAsync(workspace, turnCopy.Id, CancellationToken.None);
        persisted!.Metadata!["trusted"].Should().Be("[]", "revoking trust during a turn must not be undone by the turn's save");
        persisted.Metadata["lastTurnId"].Should().Be("turn-1", "the turn's own changes are kept");
    }

    [Fact]
    public async Task Deliberate_change_after_the_edit_wins_and_becomes_the_latest_value()
    {
        var turnCopy = await SeedAsync(new Dictionary<string, string> { ["todos"] = "start" });

        await EditAsync(session => With(session, "todos", "external"));
        var fresh = await store.GetByIdAsync(workspace, turnCopy.Id, CancellationToken.None);
        await store.SaveAsync(workspace, With(fresh!, "todos", "agent"), CancellationToken.None);
        await store.SaveAsync(workspace, turnCopy, CancellationToken.None);

        var persisted = await store.GetByIdAsync(workspace, turnCopy.Id, CancellationToken.None);
        persisted!.Metadata!["todos"].Should().Be("agent");
    }

    [Fact]
    public async Task Removed_keys_and_titles_are_carried_forward()
    {
        var turnCopy = await SeedAsync(new Dictionary<string, string> { ["model"] = "big" });

        await EditAsync(session =>
        {
            var metadata = new Dictionary<string, string>(session.Metadata!);
            metadata.Remove("model");
            return session with { Title = "Imported work item", Metadata = metadata };
        });
        await store.SaveAsync(workspace, turnCopy, CancellationToken.None);

        var persisted = await store.GetByIdAsync(workspace, turnCopy.Id, CancellationToken.None);
        persisted!.Metadata!.Should().NotContainKey("model");
        persisted.Title.Should().Be("Imported work item");
    }

    [Fact]
    public async Task Reset_starts_a_new_window()
    {
        var turnCopy = await SeedAsync(new Dictionary<string, string> { ["key"] = "a" });

        await EditAsync(session => With(session, "key", "b"));
        await coordinator.ResetAsync(workspace, turnCopy.Id, CancellationToken.None);
        await store.SaveAsync(workspace, turnCopy, CancellationToken.None);

        var persisted = await store.GetByIdAsync(workspace, turnCopy.Id, CancellationToken.None);
        persisted!.Metadata!["key"].Should().Be("a");
    }

    public void Dispose()
    {
        TestDirectoryCleanup.DeleteIfExists(workspace);
        TestDirectoryCleanup.DeleteIfExists(userRoot);
    }

    private async Task<ConversationSession> SeedAsync(Dictionary<string, string> metadata)
    {
        var session = new ConversationSession(
            Id: $"session-{Guid.NewGuid():N}",
            Title: "Original",
            State: SessionLifecycleState.Active,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            WorkingDirectory: workspace,
            RepositoryRoot: workspace,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            ActiveTurnId: null,
            LastCheckpointId: null,
            Metadata: metadata);
        await store.SaveAsync(workspace, session, CancellationToken.None);
        return session;
    }

    private async Task EditAsync(Func<ConversationSession, ConversationSession> change)
    {
        var id = (await store.ListAllAsync(workspace, CancellationToken.None)).Single().Id;
        await coordinator.RunAsync(workspace, id, async ct =>
        {
            var current = await store.GetByIdAsync(workspace, id, ct);
            await store.SaveAsync(workspace, change(current!), ct);
            return true;
        }, CancellationToken.None);
    }

    private static ConversationSession With(ConversationSession session, string key, string value)
    {
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(session.Metadata);
        metadata[key] = value;
        return session with { Metadata = metadata };
    }
}
