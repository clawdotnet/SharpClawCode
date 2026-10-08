using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using SharpClaw.Code.Cli;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Mcp.Server;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Mcp;

/// <summary>Exercises the inbound MCP transport with real SDK clients.</summary>
public sealed class InboundMcpHttpTests
{
    /// <summary>Verifies permissions, protocol contracts, and transport lifecycle.</summary>
    [Fact]
    public async Task Loopback_http_uses_same_surface_rejects_bad_host_origin_and_malformed_requests_then_shuts_down()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        await using var server = await HttpHost.StartAsync(new(fixture.Root, Transport: SharpClawMcpTransport.Http, Port: FreePort()));
        using var http = new HttpClient();
        using var badHost = new HttpRequestMessage(HttpMethod.Post, server.Endpoint) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        badHost.Headers.Host = "evil.example:" + server.Endpoint.Port;
        (await http.SendAsync(badHost)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var badOrigin = new HttpRequestMessage(HttpMethod.Post, server.Endpoint) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        badOrigin.Headers.Add("Origin", "https://evil.example");
        (await http.SendAsync(badOrigin)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var malformed = new HttpRequestMessage(HttpMethod.Post, server.Endpoint) { Content = new StringContent("{", Encoding.UTF8, "application/json") };
        malformed.Headers.Accept.ParseAdd("application/json"); malformed.Headers.Accept.ParseAdd("text/event-stream");
        var badResponse = await http.SendAsync(malformed);
        badResponse.IsSuccessStatusCode.Should().BeFalse();
        await using var client = await ClientAsync(server.Endpoint);
        (await client.ListToolsAsync()).Should().HaveCount(8);
        (await client.CallToolAsync("symbol_search", new Dictionary<string, object?> { ["query"] = "Invoice" })).IsError.Should().NotBeTrue();
        (await client.CallToolAsync("verify_workspace")).IsError.Should().BeTrue();
        (await client.CallToolAsync("csharp_rename_symbol")).IsError.Should().BeTrue();
    }

    /// <summary>Verifies permissions, protocol contracts, and transport lifecycle.</summary>
    [Fact]
    public async Task Elevated_http_requires_token_and_keeps_client_durable_sessions_isolated()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        const string token = "test-only-mcp-token-927354";
        await using var server = await HttpHost.StartAsync(new(fixture.Root, PermissionMode.DangerFullAccess, true,
            Transport: SharpClawMcpTransport.Http, Port: FreePort(), BearerToken: token));
        using var http = new HttpClient();
        (await http.GetAsync(server.Endpoint)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var wrong = new HttpRequestMessage(HttpMethod.Get, server.Endpoint);
        wrong.Headers.Authorization = new("Bearer", "incorrect-test-token");
        (await http.SendAsync(wrong)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await using var first = await ClientAsync(server.Endpoint, token);
        await using var second = await ClientAsync(server.Endpoint, token);
        (await first.ListToolsAsync()).Should().HaveCount(9);
        (await first.CallToolAsync("dotnet_solution_inspect")).IsError.Should().NotBeTrue();
        (await second.CallToolAsync("workspace_search", new Dictionary<string, object?> { ["query"] = "Invoice" })).IsError.Should().NotBeTrue();
        var sessions = await server.Host.Services.GetRequiredService<ISessionStore>().ListAllAsync(fixture.Root, CancellationToken.None);
        sessions.Should().HaveCount(2);
        sessions.Select(s => s.Id).Distinct().Should().HaveCount(2);
        // Cancellation of a client request cannot poison the connection or change its selected permissions.
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Func<Task> call = async () => await first.CallToolAsync("verify_workspace", cancellationToken: cancelled.Token);
        await call.Should().ThrowAsync<OperationCanceledException>();
        (await first.ListToolsAsync()).Should().HaveCount(9);
    }

    /// <summary>Rejects unsafe or unauthenticated HTTP exposure.</summary>
    [Theory]
    [InlineData("0.0.0.0", false, null)]
    [InlineData("::", true, "test-only-token-123456")]
    [InlineData("192.0.2.1", false, "test-only-token-123456")]
    [InlineData("192.0.2.1", true, null)]
    [InlineData("localhost", false, "short")]
    public void Invalid_exposure_is_rejected_before_listening(string address, bool remote, string? token)
        => FluentActions.Invoking(() => SharpClawMcpServer.ValidateHttpOptions(new(".", Host: address, AllowRemote: remote, BearerToken: token)))
            .Should().Throw<ArgumentException>();

    private static Task<McpClient> ClientAsync(Uri endpoint, string? token = null)
        => McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint, TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = token is null ? null : new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
        }));

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private sealed class HttpHost : IAsyncDisposable
    {
        internal IHost Host { get; }
        internal Uri Endpoint { get; }
        private readonly CancellationTokenSource stop = new();
        private readonly Task run;
        private HttpHost(SharpClawMcpServerOptions options)
        {
            Host = CliHostBuilder.BuildHost([]);
            Endpoint = new Uri($"http://127.0.0.1:{options.Port}/mcp");
            run = Host.Services.GetRequiredService<ISharpClawMcpServer>().RunAsync(options, stop.Token);
        }
        internal static async Task<HttpHost> StartAsync(SharpClawMcpServerOptions options)
        {
            var server = new HttpHost(options);
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(250) };
                while (true)
                {
                    if (server.run.IsCompleted) { await server.run; throw new InvalidOperationException("HTTP server stopped before listening."); }
                    try { using var response = await http.GetAsync(server.Endpoint, timeout.Token); break; }
                    catch (HttpRequestException) { await Task.Delay(50, timeout.Token); }
                    catch (TaskCanceledException) when (!timeout.IsCancellationRequested) { }
                }
                return server;
            }
            catch { await server.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { Host.Dispose(); stop.Dispose(); }
        }
    }
}
