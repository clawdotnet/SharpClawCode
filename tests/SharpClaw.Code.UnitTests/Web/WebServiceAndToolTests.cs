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

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void WebFetchNetworkPolicy_should_only_allow_public_addresses(string address, bool expected)
        => SharpClaw.Code.Web.Fetch.WebFetchNetworkPolicy.IsPublicAddress(IPAddress.Parse(address)).Should().Be(expected);

    [Fact]
    public async Task WebFetch_handler_should_refuse_loopback_and_metadata_targets()
    {
        using var client = new HttpClient(SharpClaw.Code.Web.Fetch.WebFetchNetworkPolicy.CreateHandler(allowPrivateNetworkAccess: false));
        IWebFetchService service = new WebFetchService(client);

        var loopback = async () => await service.FetchAsync("http://127.0.0.1:9/", CancellationToken.None);
        var metadata = async () => await service.FetchAsync("http://169.254.169.254/latest/meta-data/", CancellationToken.None);
        var localhost = async () => await service.FetchAsync("http://localhost:9/", CancellationToken.None);

        (await loopback.Should().ThrowAsync<HttpRequestException>()).WithMessage("*non-public*");
        (await metadata.Should().ThrowAsync<HttpRequestException>()).WithMessage("*non-public*");
        (await localhost.Should().ThrowAsync<HttpRequestException>()).WithMessage("*non-public*");
    }

    [Fact]
    public async Task WebFetchService_should_reject_non_http_schemes_and_truncate_large_bodies()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('a', 10_000)),
        });
        IWebFetchService service = new WebFetchService(
            new HttpClient(handler),
            Options.Create(new WebFetchOptions { MaxResponseBytes = 100 }));

        var file = async () => await service.FetchAsync("file:///etc/passwd", CancellationToken.None);
        await file.Should().ThrowAsync<ArgumentException>();

        var document = await service.FetchAsync("https://example.com/large", CancellationToken.None);
        document.Content.Should().Contain("truncated at 100 bytes");
        document.Content.Length.Should().BeLessThan(200);
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
