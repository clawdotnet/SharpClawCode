using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Specs;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies spec-mode parsing, rendering, and artifact persistence.
/// </summary>
public sealed class SpecWorkflowServiceTests
{
    [Fact]
    public async Task MaterializeAsync_should_write_spec_documents_and_suffix_collisions()
    {
        ISpecWorkflowService service = new SpecWorkflowService(new LocalFileSystem(), new PathService(), new FixedClock());
        var workspace = CreateWorkspace();
        var payload = CreatePayloadJson();

        var first = await service.MaterializeAsync(workspace, "Add offline sync support", payload, CancellationToken.None);
        var second = await service.MaterializeAsync(workspace, "Add offline sync support", payload, CancellationToken.None);

        first.RootPath.Should().EndWith("/2026-04-08-add-offline-sync-support");
        second.RootPath.Should().EndWith("/2026-04-08-add-offline-sync-support-2");
        File.ReadAllText(first.RequirementsPath).Should().Contain("## Requirements");
        File.ReadAllText(first.RequirementsPath).Should().Contain("the system shall");
        File.ReadAllText(first.DesignPath).Should().Contain("## Architecture");
        File.ReadAllText(first.TasksPath).Should().Contain("- [ ] **TASK-001**");
    }

    [Fact]
    public async Task MaterializeAsync_should_fail_without_creating_partial_folder_when_payload_is_invalid()
    {
        ISpecWorkflowService service = new SpecWorkflowService(new LocalFileSystem(), new PathService(), new FixedClock());
        var workspace = CreateWorkspace();

        var act = async () => await service.MaterializeAsync(workspace, "Spec this", "{not json", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.Exists(Path.Combine(workspace, "docs", "superpowers", "specs")).Should().BeFalse();
    }

    [Fact]
    public async Task MaterializeAsync_should_clean_up_partial_folder_when_file_write_fails()
    {
        var fileSystem = new FailingWriteFileSystem();
        ISpecWorkflowService service = new SpecWorkflowService(fileSystem, new PathService(), new FixedClock());
        var workspace = CreateWorkspace();
        var expectedRoot = Path.Combine(workspace, "docs", "superpowers", "specs", "2026-04-08-add-offline-sync-support");

        var act = async () => await service.MaterializeAsync(workspace, "Add offline sync support", CreatePayloadJson(), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        Directory.Exists(expectedRoot).Should().BeFalse();
        fileSystem.DeletedDirectories.Should().Contain(expectedRoot);
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-spec-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    private static string CreatePayloadJson()
        => """
           {
             "requirements": {
               "title": "Offline Sync Requirements",
               "summary": "Support queued changes while the client is disconnected.",
               "requirements": [
                 {
                   "id": "REQ-001",
                   "statement": "When the client is offline, the system shall queue write operations for later synchronization.",
                   "rationale": "Users must be able to continue working."
                 }
               ]
             },
             "design": {
               "title": "Offline Sync Design",
               "summary": "Introduce a local queue and replay workflow.",
               "architecture": ["Add a sync queue service in the runtime layer."],
               "dataFlow": ["Write requests are persisted locally, then replayed when connectivity returns."],
               "interfaces": ["Expose queue status through the existing operational surfaces."],
               "failureModes": ["Handle replay conflicts by surfacing a recoverable error state."],
               "testing": ["Add unit and integration coverage for queue persistence and replay."]
             },
             "tasks": {
               "title": "Offline Sync Tasks",
               "tasks": [
                 {
                   "id": "TASK-001",
                   "description": "Add queue persistence and replay orchestration.",
                   "doneCriteria": "Queued operations survive restart and replay successfully."
                 }
               ]
             }
           }
           """;

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => new(2026, 4, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FailingWriteFileSystem : IFileSystem
    {
        private readonly LocalFileSystem inner = new();

        public List<string> DeletedDirectories { get; } = [];

        public Task<IAsyncDisposable> AcquireExclusiveFileLockAsync(string lockFilePath, CancellationToken cancellationToken = default)
            => inner.AcquireExclusiveFileLockAsync(lockFilePath, cancellationToken);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);

        public IEnumerable<string> EnumerateFiles(string path, string searchPattern) => inner.EnumerateFiles(path, searchPattern);

        public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
            => inner.ReadAllTextIfExistsAsync(path, cancellationToken);

        public Task<string[]> ReadAllLinesIfExistsAsync(string path, CancellationToken cancellationToken)
            => inner.ReadAllLinesIfExistsAsync(path, cancellationToken);

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
            => path.EndsWith("design.md", StringComparison.Ordinal)
                ? Task.FromException(new IOException("boom"))
                : inner.WriteAllTextAsync(path, content, cancellationToken);

        public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
            => inner.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

        public Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
            => inner.AppendLineAsync(path, line, cancellationToken);

        public void DeleteDirectoryRecursive(string path)
        {
            DeletedDirectories.Add(path);
            inner.DeleteDirectoryRecursive(path);
        }

        public void TryDeleteFile(string path) => inner.TryDeleteFile(path);
    }
}
