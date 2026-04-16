using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.UnitTests.Commands;

public sealed class FeatureCommandHandlersTests
{
    [Fact]
    public async Task Usage_command_should_render_workspace_usage_payload()
    {
        var renderer = new RecordingRenderer();
        var handler = new UsageCommandHandler(new StubInsightsService(), new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build, "session-1");

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "usage", []), context, CancellationToken.None);

        exitCode.Should().Be(0);
        renderer.LastResult.Should().NotBeNull();
        var payload = JsonSerializer.Deserialize(renderer.LastResult!.DataJson!, ProtocolJsonContext.Default.WorkspaceUsageReport);
        payload!.WorkspaceTotal.TotalTokens.Should().Be(42);
    }

    [Fact]
    public async Task Hooks_command_should_execute_named_test_from_slash_command()
    {
        var renderer = new RecordingRenderer();
        var dispatcher = new StubHookDispatcher();
        var handler = new HooksCommandHandler(dispatcher, new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build);

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "hooks", ["test", "post-turn"]), context, CancellationToken.None);

        exitCode.Should().Be(0);
        dispatcher.LastTestName.Should().Be("post-turn");
        renderer.LastResult!.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Models_command_should_render_provider_catalog_payload()
    {
        var renderer = new RecordingRenderer();
        var handler = new ModelsCommandHandler(new StubProviderCatalogService(), new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build);

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "models", []), context, CancellationToken.None);

        exitCode.Should().Be(0);
        var payload = JsonSerializer.Deserialize(renderer.LastResult!.DataJson!, ProtocolJsonContext.Default.ListProviderModelCatalogEntry);
        payload.Should().ContainSingle(entry => entry.ProviderName == "openai-compatible" && entry.LocalRuntimeProfiles!.Length == 1);
    }

    [Fact]
    public async Task Index_command_should_render_workspace_search_payload()
    {
        var renderer = new RecordingRenderer();
        var handler = new IndexCommandHandler(new StubWorkspaceIndexService(), new StubWorkspaceSearchService(), new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build);

        var exitCode = await handler.ExecuteAsync(new SlashCommandParseResult(true, "index", ["query", "WidgetService"]), context, CancellationToken.None);

        exitCode.Should().Be(0);
        var payload = JsonSerializer.Deserialize(renderer.LastResult!.DataJson!, ProtocolJsonContext.Default.WorkspaceSearchResult);
        payload!.Hits.Should().ContainSingle(hit => hit.SymbolName == "WidgetService");
    }

    [Fact]
    public async Task Memory_command_should_save_and_list_entries()
    {
        var renderer = new RecordingRenderer();
        var store = new StubPersistentMemoryStore();
        var handler = new MemoryCommandHandler(store, new OutputRendererDispatcher([renderer]));
        var context = new CommandExecutionContext("/workspace", null, PermissionMode.WorkspaceWrite, OutputFormat.Json, PrimaryMode.Build, "session-1");

        var saveExitCode = await handler.ExecuteAsync(
            new SlashCommandParseResult(true, "memory", ["save", "User", "Prefer concise summaries"]),
            context,
            CancellationToken.None);

        saveExitCode.Should().Be(0);
        store.Entries.Should().ContainSingle(entry => entry.Scope == MemoryScope.User);

        var listExitCode = await handler.ExecuteAsync(
            new SlashCommandParseResult(true, "memory", ["list", "concise"]),
            context,
            CancellationToken.None);

        listExitCode.Should().Be(0);
        var payload = JsonSerializer.Deserialize(renderer.LastResult!.DataJson!, ProtocolJsonContext.Default.ListMemoryEntry);
        payload.Should().ContainSingle(entry => entry.Content.Contains("concise", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RecordingRenderer : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Json;

        public CommandResult? LastResult { get; private set; }

        public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
        {
            LastResult = result;
            return Task.CompletedTask;
        }

        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubInsightsService : IWorkspaceInsightsService
    {
        public Task<WorkspaceUsageReport> BuildUsageReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceUsageReport(
                workspaceRoot,
                currentSessionId,
                currentSessionId,
                new UsageSnapshot(30, 12, 0, 42, 0.05m),
                [new SessionUsageReport("session-1", "Session", true, true, new UsageSnapshot(30, 12, 0, 42, 0.05m))]));

        public Task<WorkspaceCostReport> BuildCostReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WorkspaceStatsReport> BuildStatsReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubHookDispatcher : IHookDispatcher
    {
        public string? LastTestName { get; private set; }

        public Task DispatchAsync(string workspaceRoot, HookTriggerKind trigger, string payloadJson, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<HookStatusRecord>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<HookStatusRecord>>([
                new HookStatusRecord("post-turn", HookTriggerKind.TurnCompleted, "echo", ["ok"], true)
            ]);

        public Task<HookTestResult> TestAsync(string workspaceRoot, string hookName, string payloadJson, CancellationToken cancellationToken)
        {
            LastTestName = hookName;
            return Task.FromResult(new HookTestResult(hookName, HookTriggerKind.TurnCompleted, true, "Hook executed successfully.", DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubProviderCatalogService : IProviderCatalogService
    {
        public Task<IReadOnlyList<ProviderModelCatalogEntry>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<ProviderModelCatalogEntry>>(
            [
                new ProviderModelCatalogEntry(
                    "openai-compatible",
                    "gpt-4.1-mini",
                    ["default"],
                    new AuthStatus(null, false, "openai-compatible", null, null, []),
                    SupportsToolCalls: true,
                    SupportsEmbeddings: true,
                    AvailableModels:
                    [
                        new ProviderDiscoveredModel("gpt-4.1-mini", "gpt-4.1-mini", true, true)
                    ],
                    LocalRuntimeProfiles:
                    [
                        new LocalRuntimeProfileSummary(
                            "ollama",
                            LocalRuntimeKind.Ollama,
                            "http://127.0.0.1:11434/v1/",
                            "qwen2.5-coder",
                            "nomic-embed-text",
                            ProviderAuthMode.Optional,
                            true,
                            "healthy",
                            [])
                    ])
            ]);
    }

    private sealed class StubWorkspaceIndexService : IWorkspaceIndexService
    {
        public Task<WorkspaceIndexStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceIndexStatus(workspaceRoot, DateTimeOffset.UtcNow, 4, 8, 2, 1));

        public Task<WorkspaceIndexRefreshResult> RefreshAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceIndexRefreshResult(workspaceRoot, DateTimeOffset.UtcNow, 4, 8, 2, 1, []));
    }

    private sealed class StubWorkspaceSearchService : IWorkspaceSearchService
    {
        public Task<WorkspaceSearchResult> SearchAsync(string workspaceRoot, WorkspaceSearchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceSearchResult(
                request.Query,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [new WorkspaceSearchHit("src/WidgetService.cs", WorkspaceSearchHitKind.Symbol, 1d, "WidgetService", "WidgetService", "class", 3, 3)]));
    }

    private sealed class StubPersistentMemoryStore : IPersistentMemoryStore
    {
        public List<MemoryEntry> Entries { get; } = [];

        public Task<bool> DeleteAsync(string? workspaceRoot, MemoryScope scope, string id, CancellationToken cancellationToken)
            => Task.FromResult(Entries.RemoveAll(entry => entry.Id == id && entry.Scope == scope) > 0);

        public Task<IReadOnlyList<MemoryEntry>> ListAsync(string? workspaceRoot, MemoryScope? scope, string? query, int limit, CancellationToken cancellationToken)
        {
            IEnumerable<MemoryEntry> entries = Entries;
            if (scope is not null)
            {
                entries = entries.Where(entry => entry.Scope == scope.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                entries = entries.Where(entry => entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<MemoryEntry>>(entries.Take(limit).ToArray());
        }

        public Task<MemoryEntry> SaveAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken)
        {
            Entries.RemoveAll(existing => existing.Id == entry.Id);
            Entries.Add(entry);
            return Task.FromResult(entry);
        }
    }
}
