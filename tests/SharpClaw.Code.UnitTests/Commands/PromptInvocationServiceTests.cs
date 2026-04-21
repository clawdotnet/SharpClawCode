using FluentAssertions;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.UnitTests.Commands;

/// <summary>
/// Verifies one-shot prompt execution composes stdin and routing flags correctly.
/// </summary>
public sealed class PromptInvocationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_should_use_piped_stdin_when_prompt_tokens_are_empty()
    {
        var runtime = new RecordingRuntimeCommandService();
        var renderer = new RecordingRenderer();
        var service = new PromptInvocationService(
            runtime,
            new OutputRendererDispatcher([renderer]),
            new StubCliInvocationEnvironment("review the incoming diff", isInputRedirected: true));
        var context = new CommandExecutionContext("/workspace", "model-a", PermissionMode.WorkspaceWrite, OutputFormat.Text, PrimaryMode.Build);

        var exitCode = await service.ExecuteAsync([], context, forceNonInteractive: true, CancellationToken.None);

        exitCode.Should().Be(0);
        runtime.LastPrompt.Should().Be("review the incoming diff");
        runtime.LastContext!.IsInteractive.Should().BeFalse();
        renderer.LastTurnResult.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_should_combine_piped_input_with_prompt_tokens()
    {
        var runtime = new RecordingRuntimeCommandService();
        var service = new PromptInvocationService(
            runtime,
            new OutputRendererDispatcher([new RecordingRenderer()]),
            new StubCliInvocationEnvironment("namespace Example;", isInputRedirected: true));
        var context = new CommandExecutionContext("/workspace", "model-a", PermissionMode.WorkspaceWrite, OutputFormat.Text, PrimaryMode.Build);

        var exitCode = await service.ExecuteAsync(["Summarize", "this", "file"], context, forceNonInteractive: true, CancellationToken.None);

        exitCode.Should().Be(0);
        runtime.LastPrompt.Should().Contain("Piped input:");
        runtime.LastPrompt.Should().Contain("namespace Example;");
        runtime.LastPrompt.Should().Contain("User request:");
        runtime.LastPrompt.Should().Contain("Summarize this file");
    }

    [Fact]
    public async Task ExecuteAsync_should_render_error_when_no_prompt_is_available()
    {
        var renderer = new RecordingRenderer();
        var service = new PromptInvocationService(
            new RecordingRuntimeCommandService(),
            new OutputRendererDispatcher([renderer]),
            new StubCliInvocationEnvironment(string.Empty, isInputRedirected: false));
        var context = new CommandExecutionContext("/workspace", "model-a", PermissionMode.WorkspaceWrite, OutputFormat.Text, PrimaryMode.Build);

        var exitCode = await service.ExecuteAsync([], context, forceNonInteractive: false, CancellationToken.None);

        exitCode.Should().Be(1);
        renderer.LastCommandResult!.Message.Should().Contain("No prompt text was provided");
    }

    private sealed class RecordingRuntimeCommandService : IRuntimeCommandService
    {
        public string? LastPrompt { get; private set; }

        public RuntimeCommandContext? LastContext { get; private set; }

        public Task<TurnExecutionResult> ExecutePromptAsync(string prompt, RuntimeCommandContext context, CancellationToken cancellationToken)
        {
            LastPrompt = prompt;
            LastContext = context;
            return Task.FromResult(new TurnExecutionResult(
                new ConversationSession("session-1", "Session", SessionLifecycleState.Active, PermissionMode.WorkspaceWrite, OutputFormat.Text, "/workspace", "/workspace", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null),
                new ConversationTurn("turn-1", "session-1", 1, prompt, "ok", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "primary-coding-agent", null, null, null),
                "ok",
                [],
                null,
                null,
                []));
        }

        public Task<TurnExecutionResult> ExecuteCustomCommandAsync(string commandName, string arguments, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> InspectSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ForkSessionAsync(string? sourceSessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ExportSessionAsync(string? sessionId, SessionExportFormat format, string? outputFilePath, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> UndoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> RedoAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ExportPortableSessionBundleAsync(string? sessionId, string? outputZipPath, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ImportPortableSessionBundleAsync(string bundleZipPath, bool replaceExisting, bool attachAfterImport, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ListSessionsAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> AttachSessionAsync(string sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> DetachSessionAsync(RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> ShareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> UnshareSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<CommandResult> CompactSessionAsync(string? sessionId, RuntimeCommandContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class StubCliInvocationEnvironment(string stdin, bool isInputRedirected, bool isOutputRedirected = false) : ICliInvocationEnvironment
    {
        public bool IsInputRedirected => isInputRedirected;

        public bool IsOutputRedirected => isOutputRedirected;

        public Task<string> ReadStandardInputToEndAsync(CancellationToken cancellationToken)
            => Task.FromResult(stdin);
    }

    private sealed class RecordingRenderer : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Text;

        public CommandResult? LastCommandResult { get; private set; }

        public TurnExecutionResult? LastTurnResult { get; private set; }

        public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
        {
            LastCommandResult = result;
            return Task.CompletedTask;
        }

        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
        {
            LastTurnResult = result;
            return Task.CompletedTask;
        }
    }
}
