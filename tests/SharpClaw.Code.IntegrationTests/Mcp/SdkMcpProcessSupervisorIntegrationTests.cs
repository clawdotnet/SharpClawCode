using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Mcp.FixtureServer;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Mcp.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.IntegrationTests.Mcp;

/// <summary>
/// Exercises <see cref="SdkMcpProcessSupervisor"/> against a real stdio MCP server (fixture).
/// </summary>
public sealed class SdkMcpProcessSupervisorIntegrationTests
{
    /// <summary>
    /// Runs the fixture MCP server over stdio via <c>dotnet exec</c>, lists tools, then stops the session.
    /// </summary>
    [Fact]
    public async Task StartAsync_stdio_fixture_establishes_session_lists_tools_and_stop_disposes()
    {
        var dllPath = typeof(EchoTools).Assembly.Location;
        var cwd = Path.GetDirectoryName(dllPath)!;

        var supervisor = new SdkMcpProcessSupervisor(NullLoggerFactory.Instance);
        var def = new McpServerDefinition(
            Id: "integration-fixture",
            DisplayName: "Fixture",
            TransportKind: "stdio",
            Endpoint: "dotnet",
            EnabledByDefault: true,
            Environment: null,
            Arguments: ["exec", dllPath]);

        var result = await supervisor.StartAsync(def, cwd, CancellationToken.None);

        result.Started.Should().BeTrue();
        result.HandshakeSucceeded.Should().BeTrue($"handshake failed: {result.FailureReason}");
        result.ToolCount.Should().BeGreaterThan(0);
        result.SessionHandle.Should().BeGreaterThan(0);

        await supervisor.StopAsync(new McpProcessStopRequest(Pid: null, SessionHandle: result.SessionHandle), CancellationToken.None);

        await supervisor.StopAsync(new McpProcessStopRequest(Pid: null, SessionHandle: result.SessionHandle), CancellationToken.None);
    }
}
