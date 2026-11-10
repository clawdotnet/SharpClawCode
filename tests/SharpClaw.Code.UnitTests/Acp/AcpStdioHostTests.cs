using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Runtime.Turns;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Runtime;
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
    private static readonly PathService PathService = new();

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
        using var promptInput = new StringReader("""
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"clientCapabilities":{"approvalRequests":true}}}
            {"jsonrpc":"2.0","id":2,"method":"session/prompt","params":{"cwd":"/tmp/workspace","sessionId":"session-1","model":"ollama/qwen2.5-coder","prompt":"Summarize","editorContext":{"workspaceRoot":"/tmp/another-workspace","sessionId":"unrelated","currentFilePath":"/tmp/workspace/src/App.cs","selection":{"start":0,"end":9,"text":"Summarize"}}}}
            """);
        using var promptOutput = new StringWriter(new StringBuilder());
        await host.RunAsync(promptInput, promptOutput, CancellationToken.None);

        runtime.LastRequest.Should().NotBeNull();
        runtime.LastRequest!.Metadata.Should().ContainKey("model");
        runtime.LastRequest.Metadata!["model"].Should().Be("ollama/qwen2.5-coder");
        runtime.LastRequest.IsInteractive.Should().BeTrue();
        editorBuffer.LastPublished.Should().NotBeNull();
        editorBuffer.LastPublished!.SessionId.Should().Be("session-1");
        editorBuffer.LastPublished.WorkspaceRoot.Should().Be(NormalizePath("/tmp/workspace"));
        editorBuffer.LastPublished.CurrentFilePath.Should().Be("/tmp/workspace/src/App.cs");
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

        indexService.LastWorkspaceRoot.Should().Be(NormalizePath("/tmp/workspace"));
        searchService.LastWorkspaceRoot.Should().Be(NormalizePath("/tmp/workspace"));
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
        memoryStore.LastSaveWorkspaceRoot.Should().Be(NormalizePath("/tmp/workspace"));

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

        memoryStore.LastDeleteWorkspaceRoot.Should().Be(NormalizePath("/tmp/workspace"));
        memoryStore.LastDeleteScope.Should().Be(MemoryScope.Project);
        ReadResponseResult(deleteOutput, "delete")["deleted"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task Live_controls_match_prompt_session_and_workspace_and_return_unconsumed_steering()
    {
        var controls = new LiveTurnControlService();
        var runtime = new StubConversationRuntime
        {
            DuringPrompt = async (request, token) =>
            {
                var evt = new TurnSteeredEvent("event", request.SessionId!, "turn", DateTimeOffset.UtcNow, "private correction");
                await controls.PublishAsync(new("turnSteered", evt.OccurredAtUtc, evt, SessionId: request.SessionId), token);
                await Task.Delay(Timeout.Infinite, token);
            }
        };
        var host = CreateHost(runtime, controls: controls);
        using var input = new StringReader("""
            {"jsonrpc":"2.0","id":"init","method":"initialize","params":{}}
            {"jsonrpc":"2.0","id":"a","method":"session/prompt","params":{"cwd":".","sessionId":"one","prompt":"work","liveTurnControl":true}}
            {"jsonrpc":"2.0","id":"b","method":"session/prompt","params":{"cwd":".","sessionId":"two","prompt":"work","liveTurnControl":true}}
            {"jsonrpc":"2.0","id":"wrong-session","method":"session/cancel","params":{"cwd":".","sessionId":"two","promptRequestId":"a"}}
            {"jsonrpc":"2.0","id":"wrong-workspace","method":"session/cancel","params":{"cwd":"another","sessionId":"one","promptRequestId":"a"}}
            {"jsonrpc":"2.0","id":"wrong-id","method":"session/cancel","params":{"cwd":".","sessionId":"one","promptRequestId":"previous"}}
            {"jsonrpc":"2.0","id":"steer-a","method":"session/steer","params":{"cwd":".","sessionId":"one","promptRequestId":"a","text":"keep API"}}
            {"jsonrpc":"2.0","id":"cancel-a","method":"session/cancel","params":{"cwd":".","sessionId":"one","promptRequestId":"a"}}
            {"jsonrpc":"2.0","id":"steer-b","method":"session/steer","params":{"cwd":".","sessionId":"two","promptRequestId":"b","text":"add tests"}}
            """);
        using var output = new StringWriter();
        await host.RunAsync(input, output, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        ReadResponseResult(output, "init")["agentCapabilities"]!["liveTurnControl"]!.GetValue<bool>().Should().BeTrue();
        foreach (var id in new[] { "wrong-session", "wrong-workspace", "wrong-id" })
            ReadResponseResult(output, id)["accepted"]!.GetValue<bool>().Should().BeFalse();
        foreach (var id in new[] { "steer-a", "cancel-a", "steer-b" })
            ReadResponseResult(output, id)["accepted"]!.GetValue<bool>().Should().BeTrue();
        ReadResponseResult(output, "a")["stopReason"]!.GetValue<string>().Should().Be("cancelled");
        ReadResponseResult(output, "a")["pendingSteering"]![0]!.GetValue<string>().Should().Be("keep API");
        ReadResponseResult(output, "b")["stopReason"]!.GetValue<string>().Should().Be("cancelled", "EOF releases the other live turn");
        ReadResponseResult(output, "b")["pendingSteering"]![0]!.GetValue<string>().Should().Be("add tests");
        var notifications = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!).Where(message => message["method"] is not null).ToArray();
        notifications.Select(message => (message["params"]!["sessionId"]!.GetValue<string>(), message["params"]!["promptRequestId"]!.GetValue<string>()))
            .Should().BeEquivalentTo([("one", "a"), ("two", "b")]);
        output.ToString().Should().NotContain("private correction");
    }

    [Theory]
    [InlineData("""{"sessionId":"one"}""")]
    [InlineData("""{"sessionId":"one","cwd":"."}""")]
    public async Task Spec_shaped_cancel_notification_cancels_the_sessions_live_prompt(string cancelParams)
    {
        var cancelledBeforeEof = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new StubConversationRuntime
        {
            DuringPrompt = async (request, token) =>
            {
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { cancelledBeforeEof.TrySetResult(); throw; }
            }
        };
        var host = CreateHost(runtime, controls: new LiveTurnControlService());
        var input = new GatedLineReader(
            """{"jsonrpc":"2.0","id":"a","method":"session/prompt","params":{"cwd":".","sessionId":"one","prompt":"work","liveTurnControl":true}}""",
            $$"""{"jsonrpc":"2.0","method":"session/cancel","params":{{cancelParams}}}""");
        using var output = new StringWriter();

        var run = host.RunAsync(input, output, CancellationToken.None);
        var completed = await Task.WhenAny(cancelledBeforeEof.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        input.Release();
        await run.WaitAsync(TimeSpan.FromSeconds(10));

        completed.Should().BeSameAs(cancelledBeforeEof.Task, "the notification, not EOF, must cancel the prompt");
        ReadResponseResult(output, "a")["stopReason"]!.GetValue<string>().Should().Be("cancelled");
        output.ToString().Should().NotContain("\"error\"");
    }

    /// <summary>Serves fixed lines, then blocks reads until released so EOF cannot be what cancels a prompt.</summary>
    private sealed class GatedLineReader(params string[] lines) : TextReader
    {
        private readonly Queue<string> pending = new(lines);
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => gate.TrySetResult();

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (pending.TryDequeue(out var line)) return line;
            await gate.Task.WaitAsync(cancellationToken);
            return null;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancellation_or_EOF_settles_a_pending_approval(bool cancel)
    {
        var approvals = new AcpApprovalCoordinator();
        bool? approved = null;
        var runtime = new StubConversationRuntime
        {
            DuringPrompt = async (request, token) =>
            {
                var decision = await approvals.RequestApprovalAsync(new(ApprovalScope.ShellExecution, "bash", "Allow?", "test", true),
                    new(request.SessionId!, ".", ".", PermissionMode.WorkspaceWrite, null, false, true, PermissionRequestSourceKind.Runtime, "acp", null, null), token);
                approved = decision.Approved;
            }
        };
        var host = CreateHost(runtime, controls: new LiveTurnControlService(), approvals: approvals);
        var lines = """
            {"jsonrpc":"2.0","id":"init","method":"initialize","params":{"clientCapabilities":{"approvalRequests":true}}}
            {"jsonrpc":"2.0","id":"prompt","method":"session/prompt","params":{"cwd":".","sessionId":"s","prompt":"run"}}
            """;
        if (cancel) lines = lines.Replace("\"prompt\":\"run\"", "\"prompt\":\"run\",\"liveTurnControl\":true") + "\n" +
            """{"jsonrpc":"2.0","id":"cancel","method":"session/cancel","params":{"cwd":".","sessionId":"s","promptRequestId":"prompt"}}""";
        using var output = new StringWriter();
        await host.RunAsync(new StringReader(lines), output, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var notification = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!).Single(message => message["params"]?["update"]?["sessionUpdate"]?.GetValue<string>() == "approvalRequest");
        approvals.TryResolve(notification["params"]!["update"]!["approval"]!["requestId"]!.GetValue<string>(), true, true).Should().BeFalse();
        ReadResponseResult(output, "prompt")["stopReason"]!.GetValue<string>().Should().Be(cancel ? "cancelled" : "endTurn");
        approved.Should().NotBe(true);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("other", false)]
    [InlineData("tenant", true)]
    public async Task Required_host_identity_is_checked_before_an_editor_can_grant_approval(string? identityTenant, bool allowed)
    {
        using var services = new ServiceCollection().AddSharpClawRuntime().AddSharpClawAcp().BuildServiceProvider();
        var principal = identityTenant is null ? null : new ApprovalPrincipal("editor-user", TenantId: identityTenant);
        using var identity = services.GetRequiredService<IApprovalPrincipalAccessor>().BeginScope(principal,
            new(ApprovalAuthMode.TrustedHeader, true, true, RequireAuthenticatedApprovals: true));
        var coordinator = services.GetRequiredService<AcpApprovalCoordinator>();
        var shown = false;
        coordinator.Configure(true, notification =>
        {
            shown = true;
            coordinator.TryResolve(notification["params"]!["update"]!["approval"]!["requestId"]!.GetValue<string>(), true, false);
            return Task.CompletedTask;
        });
        var result = await services.GetRequiredService<IApprovalService>().RequestApprovalAsync(
            new(ApprovalScope.ShellExecution, "bash", "Allow?", "test", false),
            new("s", ".", ".", PermissionMode.WorkspaceWrite, null, false, true, PermissionRequestSourceKind.Runtime, "acp", null, null, TenantId: "tenant"),
            CancellationToken.None);
        result.Approved.Should().Be(allowed);
        shown.Should().Be(allowed);
        if (allowed) result.Principal.Should().Be(principal);
    }

    private static AcpStdioHost CreateHost(
        StubConversationRuntime? runtime = null,
        StubEditorContextBuffer? editorBuffer = null,
        StubWorkspaceIndexService? workspaceIndexService = null,
        StubWorkspaceSearchService? workspaceSearchService = null,
        StubPersistentMemoryStore? persistentMemoryStore = null,
        StubProviderCatalogService? providerCatalogService = null,
        LiveTurnControlService? controls = null, AcpApprovalCoordinator? approvals = null)
        => new(
            runtime ?? new StubConversationRuntime(),
            new StubAttachmentStore(),
            editorBuffer ?? new StubEditorContextBuffer(),
            workspaceIndexService ?? new StubWorkspaceIndexService(),
            workspaceSearchService ?? new StubWorkspaceSearchService(),
            persistentMemoryStore ?? new StubPersistentMemoryStore(),
            providerCatalogService ?? new StubProviderCatalogService(),
            approvals ?? new AcpApprovalCoordinator(),
            new PathService(),
            NullLogger<AcpStdioHost>.Instance, controls);

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

    private static string NormalizePath(string path)
        => PathService.GetFullPath(path);

    private sealed class StubConversationRuntime : IConversationRuntime
    {
        public Func<RunPromptRequest, CancellationToken, Task>? DuringPrompt { get; init; }
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

        public async Task<TurnExecutionResult> RunPromptAsync(RunPromptRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (DuringPrompt is not null) await DuringPrompt(request, cancellationToken);
            return new TurnExecutionResult(
                new ConversationSession("session-1", "Prompt", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Json, request.WorkingDirectory ?? "/tmp/workspace", request.WorkingDirectory ?? "/tmp/workspace", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null),
                new ConversationTurn("turn-1", "session-1", 1, request.Prompt, "ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "primary-coding-agent", null, null, null),
                "ok",
                [],
                null,
                null,
                []);
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
