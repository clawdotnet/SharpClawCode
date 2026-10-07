using System.Net;
using System.Text;
using System.Text.Json;
using McpToolAgent;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.UnitTests.Mcp;

public sealed class ParallelSearchExampleTests
{
    [Theory]
    [InlineData("web_search", "{\"objective\":\"C# cancellation\",\"search_queries\":[\"C# cancellation tokens\"]}")]
    [InlineData("web_fetch", "{\"urls\":[\"https://learn.microsoft.com/dotnet/\"]}")]
    public async Task ExecutesDiscoveredToolThroughRegistryWithAnonymousProjectHeaders(string tool, string arguments)
    {
        using var handler = new McpHandler();
        using var httpClient = new HttpClient(handler);
        var result = await ParallelSearchExample.ExecuteAsync(
            "parallel_" + tool, arguments, Context(), CancellationToken.None, httpClient);

        Assert.True(result.PermissionDecision.IsAllowed);
        Assert.True(result.Result.Succeeded);
        Assert.Contains("https://learn.microsoft.com/dotnet/", result.Result.Output);
        Assert.NotNull(result.Result.StructuredOutputJson);
        Assert.Contains(handler.Requests, request => request.Method == "initialize");
        Assert.Contains(handler.Requests, request => request.Method == "tools/list");
        var call = Assert.Single(handler.Requests.Where(request => request.Method == "tools/call"));
        using var payload = JsonDocument.Parse(call.Body);
        Assert.Equal(tool, payload.RootElement.GetProperty("params").GetProperty("name").GetString());
        Assert.Equal(JsonSerializer.Serialize(JsonDocument.Parse(arguments).RootElement),
            JsonSerializer.Serialize(payload.RootElement.GetProperty("params").GetProperty("arguments")));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(ParallelSearchExample.Endpoint, request.Url);
            Assert.Equal(ParallelSearchExample.UserAgent, request.UserAgent);
            Assert.Null(request.Authorization);
        });
    }

    [Fact]
    public async Task DeniedAllowlistPreventsRemoteToolCall()
    {
        using var handler = new McpHandler();
        using var httpClient = new HttpClient(handler);
        var result = await ParallelSearchExample.ExecuteAsync(
            "parallel_web_search", "{}", Context() with { AllowedTools = ["echo"] }, CancellationToken.None, httpClient);

        Assert.False(result.PermissionDecision.IsAllowed);
        Assert.False(result.Result.Succeeded);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "tools/call");
    }

    [Fact]
    public async Task RemoteToolErrorRemainsFailure()
    {
        using var handler = new McpHandler { ToolError = true };
        using var httpClient = new HttpClient(handler);
        var result = await ParallelSearchExample.ExecuteAsync(
            "parallel_web_fetch", "{}", Context(), CancellationToken.None, httpClient);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("fixture error", result.Result.ErrorMessage);
    }

    private static ToolExecutionContext Context()
    {
        var workspace = Path.GetTempPath();
        return new("parallel-test", "test-turn", workspace, workspace,
            PermissionMode.ReadOnly, OutputFormat.Text, null,
            AllowedTools: ["parallel_web_search", "parallel_web_fetch"], IsInteractive: false);
    }

    private sealed record ObservedRequest(Uri? Url, string UserAgent, string? Authorization, string Method, string Body);

    private sealed class McpHandler : HttpMessageHandler
    {
        public List<ObservedRequest> Requests { get; } = [];
        public bool ToolError { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var method = root.GetProperty("method").GetString()!;
            Requests.Add(new(request.RequestUri, request.Headers.UserAgent.ToString(),
                request.Headers.Authorization?.ToString(), method, body));
            if (!root.TryGetProperty("id", out var id))
            {
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }

            object result = method switch
            {
                "initialize" => new
                {
                    protocolVersion = root.GetProperty("params").GetProperty("protocolVersion").GetString(),
                    capabilities = new { tools = new { } },
                    serverInfo = new { name = "parallel-fixture", version = "1.0.0" }
                },
                "tools/list" => new
                {
                    tools = new[] { "web_search", "web_fetch" }.Select(name => new
                    {
                        name, description = "Fixture tool", inputSchema = new { type = "object" }
                    })
                },
                "tools/call" => new
                {
                    content = new[] { new { type = "text", text = ToolError ? "fixture error" : "C# documentation https://learn.microsoft.com/dotnet/" } },
                    isError = ToolError
                },
                _ => throw new InvalidOperationException("Unexpected MCP method: " + method)
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }), Encoding.UTF8, "application/json")
            };
        }
    }
}
