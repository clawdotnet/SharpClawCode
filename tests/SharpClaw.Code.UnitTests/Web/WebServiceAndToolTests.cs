using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Tools;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Web;
using SharpClaw.Code.Web.Abstractions;
using SharpClaw.Code.Web.Configuration;
using SharpClaw.Code.Web.Models;
using SharpClaw.Code.Web.Services;

namespace SharpClaw.Code.UnitTests.Web;

/// <summary>
/// Verifies the first structured web services and tools.
/// </summary>
public sealed class WebServiceAndToolTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ensures the web search service parses structured results from a conservative HTML response.
    /// </summary>
    [Fact]
    public async Task WebSearchService_should_parse_results_from_html_response()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                <html><body>
                <a class="result__a" href="https://example.com/one">First Result</a>
                <a class="result__snippet">First snippet</a>
                <a class="result__a" href="https://example.com/two">Second Result</a>
                <a class="result__snippet">Second snippet</a>
                </body></html>
                """)
        });

        IWebSearchService service = new WebSearchService(
            new HttpClient(handler),
            Options.Create(new WebSearchOptions()));

        var response = await service.SearchAsync("sharpclaw", 5, CancellationToken.None);

        response.Results.Should().HaveCount(2);
        response.Results[0].Title.Should().Be("First Result");
        response.Results[1].Snippet.Should().Be("Second snippet");
    }

    /// <summary>
    /// Ensures the web fetch service returns structured page content instead of raw HTML only.
    /// </summary>
    [Fact]
    public async Task WebFetchService_should_extract_title_and_text_content()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><head><title>SharpClaw</title></head><body><main>Hello <b>world</b>.</main></body></html>")
        });

        IWebFetchService service = new WebFetchService(new HttpClient(handler));

        var document = await service.FetchAsync("https://example.com/page", CancellationToken.None);

        document.Title.Should().Be("SharpClaw");
        document.Content.Should().Contain("Hello world.");
        document.StatusCode.Should().Be(200);
    }

    /// <summary>
    /// Ensures the tool system registers and executes the built-in web tools.
    /// </summary>
    [Fact]
    public async Task AddSharpClawTools_should_register_and_execute_web_tools()
    {
        var services = new ServiceCollection();
        services.AddSharpClawWeb();
        services.AddSharpClawTools();
        services.AddSingleton<IWebSearchService>(new StubWebSearchService());
        services.AddSingleton<IWebFetchService>(new StubWebFetchService());
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetRequiredService<IToolRegistry>();
        var names = (await registry.ListAsync(cancellationToken: CancellationToken.None))
            .Select(definition => definition.Name);
        names.Should().Contain(["web_search", "web_fetch"]);

        var executor = provider.GetRequiredService<IToolExecutor>();
        var context = new ToolExecutionContext(
            SessionId: "session-001",
            TurnId: "turn-001",
            WorkspaceRoot: "/workspace",
            WorkingDirectory: "/workspace",
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Json,
            EnvironmentVariables: null,
            AllowedTools: null,
            AllowDangerousBypass: false,
            IsInteractive: false,
            SourceKind: PermissionRequestSourceKind.Runtime,
            SourceName: null,
            TrustedPluginNames: null,
            TrustedMcpServerNames: null);

        var searchEnvelope = await executor.ExecuteAsync(
            "web_search",
            JsonSerializer.Serialize(new WebSearchToolArguments("sharpclaw", 5)),
            context,
            CancellationToken.None);
        var fetchEnvelope = await executor.ExecuteAsync(
            "web_fetch",
            JsonSerializer.Serialize(new WebFetchToolArguments("https://example.com")),
            context,
            CancellationToken.None);

        searchEnvelope.Result.Succeeded.Should().BeTrue();
        fetchEnvelope.Result.Succeeded.Should().BeTrue();

        var searchPayload = JsonSerializer.Deserialize<WebSearchToolResult>(searchEnvelope.Result.StructuredOutputJson!, JsonOptions);
        var fetchPayload = JsonSerializer.Deserialize<WebFetchToolResult>(fetchEnvelope.Result.StructuredOutputJson!, JsonOptions);

        searchPayload!.Results.Should().ContainSingle(result => result.Url == "https://example.com");
        fetchPayload!.Title.Should().Be("Example");
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private sealed class StubWebSearchService : IWebSearchService
    {
        public Task<WebSearchResponse> SearchAsync(string query, int? limit, CancellationToken cancellationToken)
            => Task.FromResult(new WebSearchResponse(query, "stub", [
                new WebSearchResult("Example", "https://example.com", "Snippet")
            ]));
    }

    private sealed class StubWebFetchService : IWebFetchService
    {
        public Task<WebFetchDocument> FetchAsync(string url, CancellationToken cancellationToken)
            => Task.FromResult(new WebFetchDocument(url, 200, "text/html", "Example", "Body"));
    }
}
