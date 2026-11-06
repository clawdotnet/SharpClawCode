using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.UnitTests.Tools;

public sealed class SnapshotEditingTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"sharpclaw-snapshot-{Guid.NewGuid():N}");
    private readonly ServiceProvider services;
    private readonly Recorder recorder = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public SnapshotEditingTests()
    {
        Directory.CreateDirectory(workspace);
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddSharpClawTools();
        services = collection.BuildServiceProvider();
    }

    [Fact]
    public async Task Batch_uses_original_lines_preserves_CRLF_and_records_one_reversible_mutation()
    {
        const string original = "same\r\nmiddle\r\nsame\r\n最後";
        await File.WriteAllTextAsync(Path.Combine(workspace, "file.txt"), original);
        var snapshot = await ReadAsync(start: 2, limit: 1);
        snapshot.Content.Should().Be("2|middle");
        snapshot.NextLine.Should().Be(3);
        var result = await EditAsync(snapshot.SnapshotId, [new(1, 1, "first\r\nextra\r\n"), new(3, 1, "third\r\n")]);
        result.Result.Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(workspace, "file.txt"))).Should().Be("first\r\nextra\r\nmiddle\r\nthird\r\n最後");
        recorder.Mutations.Should().ContainSingle().Which.ContentBefore.Should().Be(original);
        var next = JsonSerializer.Deserialize<SnapshotEditResult>(result.Result.StructuredOutputJson!, Json)!;
        next.SnapshotId.Should().Be((await ReadAsync()).SnapshotId).And.NotBe(snapshot.SnapshotId);
    }

    [Fact]
    public async Task Stale_snapshot_never_overwrites_external_edits()
    {
        var path = Path.Combine(workspace, "file.txt");
        await File.WriteAllTextAsync(path, "before\n");
        var snapshot = await ReadAsync();
        await File.WriteAllTextAsync(path, "external\n");
        var result = await EditAsync(snapshot.SnapshotId, [new(1, 1, "agent\n")]);
        result.Result.Succeeded.Should().BeFalse();
        result.Result.StructuredOutputJson.Should().Contain("staleSnapshot");
        (await File.ReadAllTextAsync(path)).Should().Be("external\n");
        recorder.Mutations.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_later_edit_rejects_entire_batch()
    {
        await File.WriteAllTextAsync(Path.Combine(workspace, "file.txt"), "one\ntwo\nthree\n");
        var snapshot = await ReadAsync();
        foreach (var edits in new SnapshotLineEdit[][]
        {
            [new(1, 1, "changed\n"), new(4, 1, "outside")],
            [new(1, 2, "changed\n"), new(2, 1, "overlap")],
            [new(1, 0, "a"), new(1, 0, "b")],
            [new(int.MaxValue, int.MaxValue, "overflow")],
        })
        {
            (await EditAsync(snapshot.SnapshotId, edits)).Result.Succeeded.Should().BeFalse();
            (await ReadAsync()).SnapshotId.Should().Be(snapshot.SnapshotId);
        }
        recorder.Mutations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PermissionMode.ReadOnly, PrimaryMode.Build)]
    [InlineData(PermissionMode.WorkspaceWrite, PrimaryMode.Plan)]
    public async Task Existing_permissions_block_snapshot_edits(PermissionMode permission, PrimaryMode mode)
    {
        await File.WriteAllTextAsync(Path.Combine(workspace, "file.txt"), "protected");
        var snapshot = await ReadAsync();
        var result = await EditAsync(snapshot.SnapshotId, [new(1, 1, "changed")], Context() with { PermissionMode = permission, PrimaryMode = mode });
        result.PermissionDecision.IsAllowed.Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(workspace, "file.txt"))).Should().Be("protected");
    }

    [Fact]
    public async Task Empty_file_insert_and_EOF_insert_are_supported()
    {
        await File.WriteAllTextAsync(Path.Combine(workspace, "file.txt"), "");
        var snapshot = await ReadAsync();
        snapshot.TotalLineCount.Should().Be(0);
        (await EditAsync(snapshot.SnapshotId, [new(1, 0, "one\n")])).Result.Succeeded.Should().BeTrue();
        snapshot = await ReadAsync();
        snapshot.TotalLineCount.Should().Be(1);
        (await EditAsync(snapshot.SnapshotId, [new(2, 0, "two")])).Result.Succeeded.Should().BeTrue();
        (await File.ReadAllTextAsync(Path.Combine(workspace, "file.txt"))).Should().Be("one\ntwo");
    }

    [Fact]
    public async Task Publication_rechecks_content_and_cancellation_cleans_staged_files()
    {
        var path = Path.Combine(workspace, "file.txt");
        await File.WriteAllTextAsync(path, "external");
        var writer = services.GetRequiredService<IConditionalFileWriter>();
        (await writer.WriteAsync(path, "stale", "agent", CancellationToken.None)).Should().BeFalse();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.WriteAsync(path, "external", "agent", cancelled.Token));
        (await File.ReadAllTextAsync(path)).Should().Be("external");
        Directory.GetFiles(workspace, ".sharpclaw-edit-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Publication_rechecks_target_after_staging()
    {
        var path = Path.Combine(workspace, "file.txt");
        await File.WriteAllTextAsync(path, "stale");
        var fileSystem = new HookedFileSystem
        {
            BeforeWrite = (target, content) => target != path && content.Length > 0 ? File.WriteAllTextAsync(path, "external") : Task.CompletedTask,
        };
        (await new ConditionalFileWriter(fileSystem).WriteAsync(path, "stale", "agent", CancellationToken.None)).Should().BeFalse();
        (await File.ReadAllTextAsync(path)).Should().Be("external");
        Directory.GetFiles(workspace, ".sharpclaw-edit-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Target_deleted_during_staging_is_reported_as_stale()
    {
        var path = Path.Combine(workspace, "file.txt");
        await File.WriteAllTextAsync(path, "before");
        var fileSystem = new HookedFileSystem
        {
            BeforeWrite = (target, _) => { if (target != path) File.Delete(path); return Task.CompletedTask; },
        };
        (await new ConditionalFileWriter(fileSystem).WriteAsync(path, "before", "agent", CancellationToken.None)).Should().BeFalse();
        File.Exists(path).Should().BeFalse();
        Directory.GetFiles(workspace, ".sharpclaw-edit-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_during_precheck_leaves_target_untouched()
    {
        var path = Path.Combine(workspace, "file.txt");
        await File.WriteAllTextAsync(path, "external");
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new HookedFileSystem { AfterRead = target => { if (target == path) cancellation.Cancel(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ConditionalFileWriter(fileSystem).WriteAsync(path, "external", "agent", cancellation.Token));
        (await File.ReadAllTextAsync(path)).Should().Be("external");
        Directory.GetFiles(workspace, ".sharpclaw-edit-*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task Snapshot_read_rejects_in_workspace_symlink_to_outside_file()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"sharpclaw-snapshot-outside-{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(outside, "outside");
            try { File.CreateSymbolicLink(Path.Combine(workspace, "link.txt"), outside); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; }
            var result = await services.GetRequiredService<IToolExecutor>().ExecuteAsync(ReadFileSnapshotTool.ToolName,
                JsonSerializer.Serialize(new ReadFileSnapshotArguments("link.txt"), Json), Context(), CancellationToken.None);
            result.Result.Succeeded.Should().BeFalse();
            result.PermissionDecision.IsAllowed.Should().BeFalse();
        }
        finally { File.Delete(outside); }
    }

    [Theory]
    [InlineData("read_file_snapshot")]
    [InlineData("edit_file_snapshot")]
    public async Task Snapshot_tools_reject_workspace_escape_at_the_permission_boundary(string toolName)
    {
        var result = await services.GetRequiredService<IToolExecutor>().ExecuteAsync(toolName,
            JsonSerializer.Serialize(new ReadFileSnapshotArguments("../outside"), Json), Context(), CancellationToken.None);
        result.Result.Succeeded.Should().BeFalse();
        result.PermissionDecision.IsAllowed.Should().BeFalse();
        result.Result.ErrorMessage.Should().Contain("workspace boundary");
    }

    private async Task<FileSnapshotResult> ReadAsync(int start = 1, int limit = 200)
    {
        var result = await services.GetRequiredService<IToolExecutor>().ExecuteAsync(ReadFileSnapshotTool.ToolName,
            JsonSerializer.Serialize(new ReadFileSnapshotArguments("file.txt", start, limit), Json), Context(), CancellationToken.None);
        result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        return JsonSerializer.Deserialize<FileSnapshotResult>(result.Result.StructuredOutputJson!, Json)!;
    }
    private Task<ToolExecutionEnvelope> EditAsync(string id, SnapshotLineEdit[] edits, ToolExecutionContext? context = null)
        => services.GetRequiredService<IToolExecutor>().ExecuteAsync(EditFileSnapshotTool.ToolName,
            JsonSerializer.Serialize(new EditFileSnapshotArguments("file.txt", id, edits), Json), context ?? Context(), CancellationToken.None);
    private ToolExecutionContext Context() => new("snapshot-session", "snapshot-turn", workspace, workspace, PermissionMode.WorkspaceWrite, OutputFormat.Json, null, IsInteractive: false, MutationRecorder: recorder);
    private sealed class Recorder : IToolMutationRecorder
    {
        public List<FileMutationOperation> Mutations { get; } = [];
        public void Record(FileMutationOperation mutation) => Mutations.Add(mutation);
    }
    public void Dispose() { services.Dispose(); Directory.Delete(workspace, recursive: true); }
    private sealed class HookedFileSystem : IFileSystem
    {
        private readonly LocalFileSystem inner = new();
        public Func<string, string, Task>? BeforeWrite { get; init; }
        public Action<string>? AfterRead { get; init; }
        public Task<IAsyncDisposable> AcquireExclusiveFileLockAsync(string lockFilePath, CancellationToken cancellationToken = default) => inner.AcquireExclusiveFileLockAsync(lockFilePath, cancellationToken);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => inner.EnumerateFiles(path, searchPattern);
        public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
        {
            var text = await inner.ReadAllTextIfExistsAsync(path, cancellationToken);
            AfterRead?.Invoke(path);
            return text;
        }
        public Task<string[]> ReadAllLinesIfExistsAsync(string path, CancellationToken cancellationToken) => inner.ReadAllLinesIfExistsAsync(path, cancellationToken);
        public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
        {
            if (BeforeWrite is not null) await BeforeWrite(path, content);
            await inner.WriteAllTextAsync(path, content, cancellationToken);
        }
        public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken) => inner.CopyFileAsync(sourcePath, destinationPath, cancellationToken);
        public Task AppendLineAsync(string path, string line, CancellationToken cancellationToken) => inner.AppendLineAsync(path, line, cancellationToken);
        public void DeleteDirectoryRecursive(string path) => inner.DeleteDirectoryRecursive(path);
        public void TryDeleteFile(string path) => inner.TryDeleteFile(path);
    }
}
