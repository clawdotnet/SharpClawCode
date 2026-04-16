using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.UnitTests.Acp;

/// <summary>
/// Verifies ACP JSON-RPC parsing and error mapping.
/// </summary>
public sealed class AcpStdioHostTests
{
    [Fact]
    public async Task RunAsync_should_return_parse_error_for_invalid_json()
    {
        var host = CreateHost();
        using var input = new StringReader("{not-json");
        using var output = new StringWriter(new StringBuilder());

        await host.RunAsync(input, output, CancellationToken.None);

        output.ToString().Should().Contain(@"""code"":-32700");
    }

    [Fact]
    public async Task RunAsync_should_return_invalid_request_for_missing_method()
    {
        var host = CreateHost();
        using var input = new StringReader("""{"jsonrpc":"2.0","id":"1"}""");
        using var output = new StringWriter(new StringBuilder());

        await host.RunAsync(input, output, CancellationToken.None);

        output.ToString().Should().Contain(@"""code"":-32600");
    }

    [Fact]
    public async Task RunAsync_should_return_method_not_found_for_unknown_method()
    {
        var host = CreateHost();
        using var input = new StringReader("""{"jsonrpc":"2.0","id":"1","method":"unknown","params":{}}""");
        using var output = new StringWriter(new StringBuilder());

        await host.RunAsync(input, output, CancellationToken.None);

        output.ToString().Should().Contain(@"""code"":-32601");
    }

    [Fact]
    public async Task RunAsync_should_flow_model_and_editor_context_into_prompt_requests()
    {
        var runtime = new StubConversationRuntime();
        var editorBuffer = new StubEditorContextBuffer();
        var host = CreateHost(runtime, editorBuffer);
        using var initInput = new StringReader("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientCapabilities":{"approvalRequests":true}}}""");
        using var initOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(initInput, initOutput, CancellationToken.None);

        using var promptInput = new StringReader("""{"jsonrpc":"2.0","id":2,"method":"session/prompt","params":{"cwd":"/tmp/workspace","sessionId":"session-1","model":"ollama/qwen2.5-coder","prompt":"Summarize","editorContext":{"workspaceRoot":"/tmp/workspace","currentFilePath":"/tmp/workspace/src/App.cs","selection":{"start":0,"end":9,"text":"Summarize"}}}}""");
        using var promptOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(promptInput, promptOutput, CancellationToken.None);

        runtime.LastRequest.Should().NotBeNull();
        runtime.LastRequest!.Metadata.Should().ContainKey("model");
        runtime.LastRequest.Metadata!["model"].Should().Be("ollama/qwen2.5-coder");
        runtime.LastRequest.IsInteractive.Should().BeTrue();
        editorBuffer.LastPublished.Should().NotBeNull();
        editorBuffer.LastPublished!.CurrentFilePath.Should().Be("/tmp/workspace/src/App.cs");
    }

    [Fact]
    public async Task RunAsync_should_return_provider_catalog_for_models_list()
    {
        var catalog = new StubProviderCatalogService
        {
            Entries =
            [
                new ProviderModelCatalogEntry(
                    ProviderName: "openai-compatible",
                    DefaultModel: "gpt-4.1-mini",
                    Aliases: ["default"],
                    AuthStatus: new AuthStatus(null, false, "openai-compatible", null, null, []),
                    SupportsToolCalls: true,
                    SupportsEmbeddings: true,
                    AvailableModels: [],
                    LocalRuntimeProfiles:
                    [
                        new LocalRuntimeProfileSummary(
                            Name: "ollama",
                            Kind: LocalRuntimeKind.Ollama,
                            BaseUrl: "http://127.0.0.1:11434/v1/",
                            DefaultChatModel: "qwen2.5-coder",
                            DefaultEmbeddingModel: "nomic-embed-text",
                            AuthMode: ProviderAuthMode.Optional,
                            IsHealthy: true,
                            HealthDetail: "1 model(s) discovered.",
                            AvailableModels:
                            [
                                new ProviderDiscoveredModel("qwen2.5-coder", "qwen2.5-coder", true, false)
                            ])
                    ])
            ]
        };
        var host = CreateHost(providerCatalogService: catalog);
        using var input = new StringReader("""{"jsonrpc":"2.0","id":"models","method":"models/list","params":{}}""");
        using var output = new StringWriter(new StringBuilder());

        await host.RunAsync(input, output, CancellationToken.None);

        var payload = JsonSerializer.Deserialize(
            ReadResponseResult(output, "models").ToJsonString(),
            ProtocolJsonContext.Default.ListProviderModelCatalogEntry);
        payload.Should().NotBeNull();
        payload![0].ProviderName.Should().Be("openai-compatible");
        payload[0].LocalRuntimeProfiles.Should().ContainSingle(profile => profile.Name == "ollama" && profile.IsHealthy);
    }

    [Fact]
    public async Task RunAsync_should_dispatch_workspace_index_and_search_requests()
    {
        var indexService = new StubWorkspaceIndexService();
        var searchService = new StubWorkspaceSearchService();
        var host = CreateHost(workspaceIndexService: indexService, workspaceSearchService: searchService);

        using var refreshInput = new StringReader("""{"jsonrpc":"2.0","id":"refresh","method":"workspace/index/refresh","params":{"cwd":"/tmp/workspace"}}""");
        using var refreshOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(refreshInput, refreshOutput, CancellationToken.None);

        using var searchInput = new StringReader("""{"jsonrpc":"2.0","id":"search","method":"workspace/search","params":{"cwd":"/tmp/workspace","query":"WidgetService","limit":5,"includeSymbols":true,"includeSemantic":false}}""");
        using var searchOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(searchInput, searchOutput, CancellationToken.None);

        indexService.LastWorkspaceRoot.Should().Be("/tmp/workspace");
        searchService.LastWorkspaceRoot.Should().Be("/tmp/workspace");
        searchService.LastRequest.Should().Be(new WorkspaceSearchRequest("WidgetService", 5, true, false));

        var refresh = JsonSerializer.Deserialize(
            ReadResponseResult(refreshOutput, "refresh").ToJsonString(),
            ProtocolJsonContext.Default.WorkspaceIndexRefreshResult);
        var search = JsonSerializer.Deserialize(
            ReadResponseResult(searchOutput, "search").ToJsonString(),
            ProtocolJsonContext.Default.WorkspaceSearchResult);

        refresh.Should().NotBeNull();
        refresh!.IndexedFileCount.Should().Be(3);
        search.Should().NotBeNull();
        search!.Hits.Should().ContainSingle(hit => hit.SymbolName == "WidgetService");
    }

    [Fact]
    public async Task RunAsync_should_round_trip_memory_save_list_and_delete_requests()
    {
        var memoryStore = new StubPersistentMemoryStore();
        var host = CreateHost(persistentMemoryStore: memoryStore);

        using var saveInput = new StringReader(
            """{"jsonrpc":"2.0","id":"save","method":"memory/save","params":{"cwd":"/tmp/workspace","sessionId":"session-1","request":{"scope":"Project","content":"Keep prompts concise.","source":"manual","tags":["style"],"confidence":0.8,"relatedFilePath":"src/App.cs","relatedSymbolName":"App"}}}""");
        using var saveOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(saveInput, saveOutput, CancellationToken.None);

        var saved = JsonSerializer.Deserialize(
            ReadResponseResult(saveOutput, "save").ToJsonString(),
            ProtocolJsonContext.Default.MemoryEntry);
        saved.Should().NotBeNull();
        saved!.Scope.Should().Be(MemoryScope.Project);
        saved.SourceSessionId.Should().Be("session-1");
        memoryStore.LastSaveWorkspaceRoot.Should().Be("/tmp/workspace");

        using var listInput = new StringReader("""{"jsonrpc":"2.0","id":"list","method":"memory/list","params":{"cwd":"/tmp/workspace","scope":"Project","query":"concise","limit":10}}""");
        using var listOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(listInput, listOutput, CancellationToken.None);

        var rows = JsonSerializer.Deserialize(
            ReadResponseResult(listOutput, "list").ToJsonString(),
            ProtocolJsonContext.Default.ListMemoryEntry);
        rows.Should().NotBeNull();
        rows.Should().ContainSingle(entry => entry.Id == saved.Id);

        using var deleteInput = new StringReader(
            $@"{{""jsonrpc"":""2.0"",""id"":""delete"",""method"":""memory/delete"",""params"":{{""cwd"":""/tmp/workspace"",""scope"":""Project"",""id"":""{saved.Id}""}}}}");
        using var deleteOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(deleteInput, deleteOutput, CancellationToken.None);

        memoryStore.LastDeleteWorkspaceRoot.Should().Be("/tmp/workspace");
        memoryStore.LastDeleteScope.Should().Be(MemoryScope.Project);
        ReadResponseResult(deleteOutput, "delete")["deleted"]!.GetValue<bool>().Should().BeTrue();
    }

    private static AcpStdioHost CreateHost(
        StubConversationRuntime? runtime = null,
        StubEditorContextBuffer? editorBuffer = null,
        StubWorkspaceIndexService? workspaceIndexService = null,
        StubWorkspaceSearchService? workspaceSearchService = null,
        StubPersistentMemoryStore? persistentMemoryStore = null,
        StubProviderCatalogService? providerCatalogService = null)
        => new(
            runtime ?? new StubConversationRuntime(),
            new StubAttachmentStore(),
            editorBuffer ?? new StubEditorContextBuffer(),
            workspaceIndexService ?? new StubWorkspaceIndexService(),
            workspaceSearchService ?? new StubWorkspaceSearchService(),
            persistentMemoryStore ?? new StubPersistentMemoryStore(),
            providerCatalogService ?? new StubProviderCatalogService(),
            new AcpApprovalCoordinator(),
            new PathService(),
            NullLogger<AcpStdioHost>.Instance);

    private static System.Text.Json.Nodes.JsonNode ReadResponseResult(StringWriter output, string id)
    {
        foreach (var line in output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(line) is not System.Text.Json.Nodes.JsonObject payload)
            {
                continue;
            }

            if (payload["id"]?.GetValue<string>() == id)
            {
                return payload["result"]!;
            }
        }

        throw new InvalidOperationException($"Could not find JSON-RPC response with id '{id}'.");
    }

    private sealed class StubConversationRuntime : IConversationRuntime
    {
        public RunPromptRequest? LastRequest { get; private set; }

        public Task<ConversationSession> CreateSessionAsync(string workspacePath, PermissionMode permissionMode, OutputFormat outputFormat, CancellationToken cancellationToken)
            => Task.FromResult(new ConversationSession("session-1", "Test", SessionLifecycleState.Active, permissionMode, outputFormat, workspacePath, workspacePath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null));

        public Task ExecuteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConversationSession> ForkSessionAsync(string workspacePath, string? sourceSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConversationSession?> GetLatestSessionAsync(string workspacePath, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConversationSession?> GetSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
            => Task.FromResult<ConversationSession?>(new ConversationSession(sessionId, "Loaded", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Json, workspacePath, workspacePath, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null));

        public Task<TurnExecutionResult> RunPromptAsync(RunPromptRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new TurnExecutionResult(
                new ConversationSession("session-1", "Prompt", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Json, request.WorkingDirectory ?? "/tmp/workspace", request.WorkingDirectory ?? "/tmp/workspace", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null),
                new ConversationTurn("turn-1", "session-1", 1, request.Prompt, "ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "primary-coding-agent", null, null, null),
                "ok",
                [],
                null,
                null,
                []));
        }
    }

    private sealed class StubAttachmentStore : IWorkspaceSessionAttachmentStore
    {
        public Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task SetAttachedSessionIdAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubEditorContextBuffer : IEditorContextBuffer
    {
        public EditorContextPayload? LastPublished { get; private set; }

        public EditorContextPayload? Peek(string normalizedWorkspaceRoot) => null;

        public void Publish(EditorContextPayload payload)
        {
            LastPublished = payload;
        }

        public EditorContextPayload? TryConsume(string normalizedWorkspaceRoot) => null;
    }

    private sealed class StubWorkspaceIndexService : IWorkspaceIndexService
    {
        public string? LastWorkspaceRoot { get; private set; }

        public Task<WorkspaceIndexStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new WorkspaceIndexStatus(workspaceRoot, null, 0, 0, 0, 0));

        public Task<WorkspaceIndexRefreshResult> RefreshAsync(string workspaceRoot, CancellationToken cancellationToken)
        {
            LastWorkspaceRoot = workspaceRoot;
            return Task.FromResult(new WorkspaceIndexRefreshResult(workspaceRoot, DateTimeOffset.UtcNow, 3, 6, 2, 1, []));
        }
    }

    private sealed class StubWorkspaceSearchService : IWorkspaceSearchService
    {
        public string? LastWorkspaceRoot { get; private set; }

        public WorkspaceSearchRequest? LastRequest { get; private set; }

        public Task<WorkspaceSearchResult> SearchAsync(string workspaceRoot, WorkspaceSearchRequest request, CancellationToken cancellationToken)
        {
            LastWorkspaceRoot = workspaceRoot;
            LastRequest = request;
            return Task.FromResult(new WorkspaceSearchResult(
                request.Query,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                [new WorkspaceSearchHit("src/WidgetService.cs", WorkspaceSearchHitKind.Symbol, 1.0d, "WidgetService", "WidgetService", "class", 3, 3)]));
        }
    }

    private sealed class StubPersistentMemoryStore : IPersistentMemoryStore
    {
        private readonly List<MemoryEntry> entries = [];

        public string? LastSaveWorkspaceRoot { get; private set; }

        public string? LastDeleteWorkspaceRoot { get; private set; }

        public MemoryScope? LastDeleteScope { get; private set; }

        public Task<bool> DeleteAsync(string? workspaceRoot, MemoryScope scope, string id, CancellationToken cancellationToken)
        {
            LastDeleteWorkspaceRoot = workspaceRoot;
            LastDeleteScope = scope;
            return Task.FromResult(entries.RemoveAll(entry => entry.Id == id) > 0);
        }

        public Task<IReadOnlyList<MemoryEntry>> ListAsync(string? workspaceRoot, MemoryScope? scope, string? query, int limit, CancellationToken cancellationToken)
        {
            IEnumerable<MemoryEntry> result = entries;
            if (scope is not null)
            {
                result = result.Where(entry => entry.Scope == scope.Value);
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                result = result.Where(entry => entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return Task.FromResult<IReadOnlyList<MemoryEntry>>(result.Take(limit).ToArray());
        }

        public Task<MemoryEntry> SaveAsync(string? workspaceRoot, MemoryEntry entry, CancellationToken cancellationToken)
        {
            LastSaveWorkspaceRoot = workspaceRoot;
            entries.RemoveAll(existing => existing.Id == entry.Id);
            entries.Add(entry);
            return Task.FromResult(entry);
        }
    }

    private sealed class StubProviderCatalogService : IProviderCatalogService
    {
        public IReadOnlyList<ProviderModelCatalogEntry> Entries { get; init; } = [];

        public Task<IReadOnlyList<ProviderModelCatalogEntry>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(Entries);
    }
}
