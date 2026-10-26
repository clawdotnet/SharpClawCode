using System.Net;
using System.Text;
using System.Text.Json;
using McpToolAgent;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.UnitTests.Mcp;

public sealed class DeedPageExampleTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("deed-page-test").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public async Task BuildsFileMapWithTextBinaryAndExpiry()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "<h1>Hi</h1>");
        Directory.CreateDirectory(Path.Combine(folder, "img"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "img", "dot.bin"), [0xff, 0xfe, 0x00]);
        Directory.CreateDirectory(Path.Combine(folder, ".git"));
        await File.WriteAllTextAsync(Path.Combine(folder, ".git", "config"), "secret");
        await File.WriteAllTextAsync(Path.Combine(folder, ".env"), "KEY=1");

        using var json = JsonDocument.Parse(await DeedPageExample.CreatePublishArgumentsAsync(folder, 600, CancellationToken.None));
        var files = json.RootElement.GetProperty("files");
        Assert.Equal("<h1>Hi</h1>", files.GetProperty("index.html").GetString());
        Assert.Equal("//4A", files.GetProperty("img/dot.bin").GetProperty("base64").GetString());
        Assert.Equal(2, files.EnumerateObject().Count());
        Assert.Equal(600, json.RootElement.GetProperty("ttl_seconds").GetInt32());
    }

    [Fact]
    public async Task RequiresIndexHtmlAndOmitsExpiryForPermanentSites()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "about.html"), "about");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None));

        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "home");
        using var json = JsonDocument.Parse(await DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None));
        Assert.False(json.RootElement.TryGetProperty("ttl_seconds", out _));
    }

    [Fact]
    public async Task SkipsHiddenFilesAndDotfiles()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "home");
        await File.WriteAllTextAsync(Path.Combine(folder, ".npmrc"), "dotfile-secret");
        Directory.CreateDirectory(Path.Combine(folder, ".aws"));
        await File.WriteAllTextAsync(Path.Combine(folder, ".aws", "credentials"), "dotdir-secret");
        var hidden = Path.Combine(folder, "credentials.txt");
        await File.WriteAllTextAsync(hidden, "hidden-secret");
        File.SetAttributes(hidden, File.GetAttributes(hidden) | FileAttributes.Hidden);
        var hiddenAttributeSupported = File.GetAttributes(hidden).HasFlag(FileAttributes.Hidden);

        var arguments = await DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None);
        using var json = JsonDocument.Parse(arguments);
        var names = json.RootElement.GetProperty("files").EnumerateObject().Select(file => file.Name).ToList();
        Assert.DoesNotContain(".npmrc", names);
        Assert.DoesNotContain(".aws/credentials", names);
        if (hiddenAttributeSupported)
        {
            // Windows and macOS: a Hidden file with an ordinary name is still skipped.
            Assert.Equal(["index.html"], names);
            Assert.DoesNotContain("hidden-secret", arguments);
        }
    }

    [Fact]
    public async Task SkipsSymbolicLinksThatLeadOutsideTheFolder()
    {
        var outside = Directory.CreateTempSubdirectory("deed-page-outside").FullName;
        try
        {
            var secret = Path.Combine(outside, "secret.txt");
            await File.WriteAllTextAsync(secret, "do-not-publish");
            await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "home");
            try
            {
                File.CreateSymbolicLink(Path.Combine(folder, "notes.txt"), secret);
                Directory.CreateSymbolicLink(Path.Combine(folder, "assets"), outside);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return; // Creating links needs extra rights on some Windows machines.
            }

            var arguments = await DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None);
            using var json = JsonDocument.Parse(arguments);
            var names = json.RootElement.GetProperty("files").EnumerateObject().Select(file => file.Name);
            Assert.Equal(["index.html"], names);
            Assert.DoesNotContain("do-not-publish", arguments);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsOversizedFileBeforeReadingIt()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "home");
        // A sparse 3 GB file: reading it whole would fail with an IOException instead.
        using (var stream = File.Create(Path.Combine(folder, "video.mp4")))
        {
            stream.SetLength(3L * 1024 * 1024 * 1024);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None));
        Assert.Contains("does not fit in one deed.page publish request", error.Message);
    }

    [Fact]
    public async Task LimitsTheEncodedRequestNotRawFileSizes()
    {
        await File.WriteAllTextAsync(Path.Combine(folder, "index.html"), "home");
        var binary = Path.Combine(folder, "photo.jpg");
        // 3.5 MB of raw bytes fits under the cap, but base64 grows it to about 4.7 MB.
        await File.WriteAllBytesAsync(binary, Enumerable.Repeat((byte)0xff, 3_500_000).ToArray());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None));

        await File.WriteAllBytesAsync(binary, Enumerable.Repeat((byte)0xff, 2_500_000).ToArray());
        var arguments = await DeedPageExample.CreatePublishArgumentsAsync(folder, null, CancellationToken.None);
        Assert.InRange(Encoding.UTF8.GetByteCount(arguments), 3_000_000, DeedPageExample.MaxArgumentsBytes);
    }

    [Fact]
    public async Task ApprovedPublishCallsRemoteToolWithoutCredentials()
    {
        using var handler = new McpHandler();
        using var httpClient = new HttpClient(handler);
        const string arguments = "{\"files\":{\"index.html\":\"<h1>Hi</h1>\"},\"ttl_seconds\":600}";
        var context = Context() with { ApprovalSettings = new ApprovalSettings([ApprovalScope.NetworkAccess], null) };
        var result = await DeedPageExample.ExecuteAsync(arguments, context, CancellationToken.None, httpClient);

        Assert.True(result.PermissionDecision.IsAllowed);
        Assert.True(result.Result.Succeeded);
        Assert.Contains("https://quiet-field-a1b2.deed.page/", result.Result.Output);
        var call = Assert.Single(handler.Requests, request => request.Method == "tools/call");
        using var payload = JsonDocument.Parse(call.Body);
        Assert.Equal("publish_site", payload.RootElement.GetProperty("params").GetProperty("name").GetString());
        Assert.Equal(JsonSerializer.Serialize(JsonDocument.Parse(arguments).RootElement),
            JsonSerializer.Serialize(payload.RootElement.GetProperty("params").GetProperty("arguments")));
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(DeedPageExample.Endpoint, request.Url);
            Assert.Equal(DeedPageExample.UserAgent, request.UserAgent);
            Assert.Null(request.Authorization);
        });
    }

    [Fact]
    public async Task PublishWithoutApprovalNeverReachesServer()
    {
        using var handler = new McpHandler();
        using var httpClient = new HttpClient(handler);
        var result = await DeedPageExample.ExecuteAsync(
            "{\"files\":{\"index.html\":\"x\"}}", Context(), CancellationToken.None, httpClient);

        Assert.False(result.PermissionDecision.IsAllowed);
        Assert.False(result.Result.Succeeded);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "tools/call");
    }

    [Fact]
    public async Task ReadOnlyModeBlocksPublishing()
    {
        using var handler = new McpHandler();
        using var httpClient = new HttpClient(handler);
        var context = Context() with
        {
            PermissionMode = PermissionMode.ReadOnly,
            ApprovalSettings = new ApprovalSettings([ApprovalScope.NetworkAccess], null)
        };
        var result = await DeedPageExample.ExecuteAsync(
            "{\"files\":{\"index.html\":\"x\"}}", context, CancellationToken.None, httpClient);

        Assert.False(result.PermissionDecision.IsAllowed);
        Assert.DoesNotContain(handler.Requests, request => request.Method == "tools/call");
    }

    [Fact]
    public async Task RemoteToolErrorRemainsFailure()
    {
        using var handler = new McpHandler { ToolError = true };
        using var httpClient = new HttpClient(handler);
        var context = Context() with { ApprovalSettings = new ApprovalSettings([ApprovalScope.NetworkAccess], null) };
        var result = await DeedPageExample.ExecuteAsync(
            "{\"files\":{\"index.html\":\"x\"}}", context, CancellationToken.None, httpClient);

        Assert.False(result.Result.Succeeded);
        Assert.Equal("site_too_large", result.Result.ErrorMessage);
    }

    private static ToolExecutionContext Context()
    {
        var workspace = Path.GetTempPath();
        return new("deed-page-test", "test-turn", workspace, workspace,
            PermissionMode.WorkspaceWrite, OutputFormat.Text, null,
            AllowedTools: [DeedPageExample.ToolName], IsInteractive: false);
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
                    serverInfo = new { name = "deed-page-fixture", version = "1.0.0" }
                },
                "tools/list" => new
                {
                    tools = new[] { "publish_site", "delete_site" }.Select(name => new
                    {
                        name, description = "Fixture tool", inputSchema = new { type = "object" }
                    })
                },
                "tools/call" => new
                {
                    content = new[]
                    {
                        new
                        {
                            type = "text",
                            text = ToolError ? "site_too_large" : "{\"url\":\"https://quiet-field-a1b2.deed.page/\",\"slug\":\"quiet-field-a1b2\"}"
                        }
                    },
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
