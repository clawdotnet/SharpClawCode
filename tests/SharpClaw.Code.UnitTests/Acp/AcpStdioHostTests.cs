using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
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

    private static AcpStdioHost CreateHost()
        => new(
            new StubConversationRuntime(),
            new StubAttachmentStore(),
            new PathService(),
            NullLogger<AcpStdioHost>.Instance);

    private sealed class StubConversationRuntime : IConversationRuntime
    {
        public Task<ConversationSession> CreateSessionAsync(string workspacePath, PermissionMode permissionMode, OutputFormat outputFormat, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task ExecuteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConversationSession> ForkSessionAsync(string workspacePath, string? sourceSessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConversationSession?> GetLatestSessionAsync(string workspacePath, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ConversationSession?> GetSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TurnExecutionResult> RunPromptAsync(RunPromptRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubAttachmentStore : IWorkspaceSessionAttachmentStore
    {
        public Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken)
            => Task.FromResult<string?>(null);

        public Task SetAttachedSessionIdAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
