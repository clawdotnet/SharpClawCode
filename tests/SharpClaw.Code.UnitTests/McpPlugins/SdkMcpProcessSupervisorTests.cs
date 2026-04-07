using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Mcp.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.McpPlugins;

/// <summary>
/// Verifies the official MCP SDK-backed process supervisor boundaries.
/// </summary>
public sealed class SdkMcpProcessSupervisorTests
{
    [Fact]
    public async Task StartAsync_should_reject_unknown_transport()
    {
        var supervisor = new SdkMcpProcessSupervisor(NullLoggerFactory.Instance);
        var def = new McpServerDefinition(
            Id: "tcp-mcp",
            DisplayName: "TCP",
            TransportKind: "tcp",
            Endpoint: "127.0.0.1:1234",
            EnabledByDefault: true,
            Environment: null,
            Arguments: null);

        var result = await supervisor.StartAsync(def, Environment.CurrentDirectory, CancellationToken.None);

        result.Started.Should().BeFalse();
        result.HandshakeSucceeded.Should().BeFalse();
        result.FailureReason.Should().Contain("not supported");
    }

    [Fact]
    public async Task StartAsync_http_requires_absolute_endpoint_url()
    {
        var supervisor = new SdkMcpProcessSupervisor(NullLoggerFactory.Instance);
        var def = new McpServerDefinition(
            Id: "http-mcp",
            DisplayName: "HTTP",
            TransportKind: "http",
            Endpoint: "/relative/mcp",
            EnabledByDefault: true,
            Environment: null,
            Arguments: null);

        var result = await supervisor.StartAsync(def, Environment.CurrentDirectory, CancellationToken.None);

        result.Started.Should().BeFalse();
        result.HandshakeSucceeded.Should().BeFalse();
        result.FailureReason.Should().Contain("http(s)");
    }
}
