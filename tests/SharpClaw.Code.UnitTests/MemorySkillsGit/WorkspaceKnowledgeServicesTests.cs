using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Services;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.MemorySkillsGit;

/// <summary>
/// Covers workspace indexing, hybrid search, and durable memory recall.
/// </summary>
public sealed class WorkspaceKnowledgeServicesTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-knowledge-{Guid.NewGuid():N}");
    private readonly string userRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-user-{Guid.NewGuid():N}");
    private readonly LocalFileSystem fileSystem = new();
    private readonly PathService pathService = new();

    [Fact]
    public async Task Index_search_and_memory_services_should_round_trip_expected_data()
    {
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "src", "WidgetService.cs"),
            """
            namespace Sample.App;

            public sealed class WidgetService
            {
                public string BuildWidgetPrompt(string name) => $"Widget prompt for {name}";
            }
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "README.md"),
            "The widget prompt pipeline provides semantic workspace context.");
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "src", "Sample.App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.5" />
              </ItemGroup>
            </Project>
            """);

        var store = CreateStore();
        var indexService = new WorkspaceIndexService(fileSystem, pathService, store);
        var searchService = new WorkspaceSearchService(store);
        var memoryStore = new PersistentMemoryStore(store);
        var recallService = new MemoryRecallService(memoryStore);

        var refresh = await indexService.RefreshAsync(workspaceRoot, CancellationToken.None);
        var search = await searchService.SearchAsync(
            workspaceRoot,
            new WorkspaceSearchRequest("WidgetService", 10),
            CancellationToken.None);

        refresh.IndexedFileCount.Should().BeGreaterThan(0);
        search.Hits.Should().Contain(hit => hit.Kind == WorkspaceSearchHitKind.Symbol && hit.SymbolName == "WidgetService");

        var now = DateTimeOffset.UtcNow;
        await memoryStore.SaveAsync(
            workspaceRoot,
            new MemoryEntry(
                Id: "project-memory-1",
                Scope: MemoryScope.Project,
                Content: "Widget prompts should stay concise and repo-specific.",
                Source: "unit-test",
                SourceSessionId: null,
                SourceTurnId: null,
                Tags: ["widgets"],
                Confidence: 0.9d,
                RelatedFilePath: "src/WidgetService.cs",
                RelatedSymbolName: "WidgetService",
                CreatedAtUtc: now,
                UpdatedAtUtc: now),
            CancellationToken.None);
        await memoryStore.SaveAsync(
            null,
            new MemoryEntry(
                Id: "user-memory-1",
                Scope: MemoryScope.User,
                Content: "Prefer explicit engineering language over vague summaries.",
                Source: "unit-test",
                SourceSessionId: null,
                SourceTurnId: null,
                Tags: ["style"],
                Confidence: 0.8d,
                RelatedFilePath: null,
                RelatedSymbolName: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now),
            CancellationToken.None);

        var recalled = await recallService.RecallAsync(workspaceRoot, "Write a concise widget prompt summary.", 5, CancellationToken.None);
        recalled.Should().Contain(entry => entry.Id == "project-memory-1");
        recalled.Should().Contain(entry => entry.Id == "user-memory-1");
    }

    [Fact]
    public async Task Search_should_treat_fts_operators_and_like_wildcards_as_literal_text()
    {
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "src"));
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "src", "RetryPolicy.cs"),
            """
            namespace Sample.App;

            public sealed class RetryPolicy
            {
                public int Backoff(int attempt) => attempt * 2;
            }
            """);

        var store = CreateStore();
        var indexService = new WorkspaceIndexService(fileSystem, pathService, store);
        var searchService = new WorkspaceSearchService(store);
        await indexService.RefreshAsync(workspaceRoot, CancellationToken.None);

        // Bare AND/OR/NOT/NEAR used to become FTS5 syntax errors.
        var operators = await searchService.SearchAsync(
            workspaceRoot,
            new WorkspaceSearchRequest("retry OR backoff NOT found", 10),
            CancellationToken.None);
        operators.Should().NotBeNull();

        // '_' must not act as a single-character LIKE wildcard in symbol search.
        var wildcard = await searchService.SearchAsync(
            workspaceRoot,
            new WorkspaceSearchRequest("Retry_olicy", 10),
            CancellationToken.None);
        wildcard.Hits.Should().NotContain(hit => hit.Kind == WorkspaceSearchHitKind.Symbol && hit.SymbolName == "RetryPolicy");
    }

    public void Dispose()
    {
        TestDirectoryCleanup.DeleteIfExists(workspaceRoot, clearSqlitePools: true);
        TestDirectoryCleanup.DeleteIfExists(userRoot, clearSqlitePools: true);
    }

    private IWorkspaceKnowledgeStore CreateStore()
        => new SqliteWorkspaceKnowledgeStore(fileSystem, pathService, TestRuntimeStorageResolver.Create(userRoot, pathService));
}
