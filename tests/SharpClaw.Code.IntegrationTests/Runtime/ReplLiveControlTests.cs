using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpClaw.Code.Cli.Composition;
using SharpClaw.Code.Commands;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>Validates live REPL input ownership, cancellation, follow-ups and approval routing.</summary>
public sealed class ReplLiveControlTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), $"sharpclaw-repl-live-{Guid.NewGuid():N}");
    private readonly Terminal terminal = new();
    private readonly Renderer renderer = new();

    /// <summary>A queued follow-up runs after cancellation and uses the same sole input reader.</summary>
    [Fact]
    public async Task Cancel_keeps_session_open_and_runs_queued_followup()
    {
        var provider = new Provider { BlockFirst = true };
        using var services = Services(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = services.GetRequiredService<IReplHost>().RunAsync(Context(), "first", deadline.Token);
        await provider.Started.Task.WaitAsync(deadline.Token);
        terminal.Input.Writer.TryWrite("second");
        await terminal.WaitForAsync(message => message.StartsWith("Follow-up queued"), deadline.Token);
        terminal.Input.Writer.TryWrite("/cancel");
        var result = await renderer.Results.Reader.ReadAsync(deadline.Token);
        result.Turn.Input.Should().Be("second");
        result.Turn.SequenceNumber.Should().Be(2);
        terminal.Input.Writer.TryComplete();
        (await run.WaitAsync(deadline.Token)).Should().Be(0);
        terminal.MaxReaders.Should().Be(1);
        terminal.Messages.Should().Contain(message => message.StartsWith("Turn cancelled."));
    }

    /// <summary>Ordinary text cannot grant tool permission while a live input reader owns the console.</summary>
    [Fact]
    public async Task Approval_requires_exact_pending_id_and_does_not_consume_queued_text()
    {
        var provider = new Provider { AskForShell = true };
        using var services = Services(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = services.GetRequiredService<IReplHost>().RunAsync(Context(), "request shell", deadline.Token);
        var approval = await terminal.WaitForAsync(message => message.StartsWith("Approval "), deadline.Token);
        var id = approval.Split(' ', ':')[1];
        terminal.Input.Writer.TryWrite("yes");
        await terminal.WaitForAsync(message => message.StartsWith("Follow-up queued"), deadline.Token);
        provider.Count.Should().Be(1, "ordinary input cannot approve the pending shell tool");
        terminal.Input.Writer.TryWrite("/approve unknown yes");
        await terminal.WaitForAsync(message => message.StartsWith("Use /approve"), deadline.Token);
        provider.Count.Should().Be(1);
        terminal.Input.Writer.TryWrite($"/approve {id} no");
        var first = await renderer.Results.Reader.ReadAsync(deadline.Token);
        first.ToolResults.Should().ContainSingle().Which.Succeeded.Should().BeFalse();
        var followup = await renderer.Results.Reader.ReadAsync(deadline.Token);
        followup.Turn.Input.Should().Be("yes");
        terminal.Input.Writer.TryComplete();
        (await run.WaitAsync(deadline.Token)).Should().Be(0);
        terminal.MaxReaders.Should().Be(1);
        terminal.Messages.Should().Contain(message => message.Contains("Running bash"));
    }

    /// <summary>Cancelling a pending approval releases the turn without approving its tool.</summary>
    [Fact]
    public async Task Cancel_during_approval_does_not_hang_or_execute_tool()
    {
        var provider = new Provider { AskForShell = true };
        using var services = Services(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = services.GetRequiredService<IReplHost>().RunAsync(Context(), "request shell", deadline.Token);
        await terminal.WaitForAsync(message => message.StartsWith("Approval "), deadline.Token);
        terminal.Input.Writer.TryWrite("/cancel");
        await terminal.WaitForAsync(message => message.StartsWith("Turn cancelled."), deadline.Token);
        terminal.Input.Writer.TryComplete();
        (await run.WaitAsync(deadline.Token)).Should().Be(0);
        provider.Count.Should().Be(1);
        renderer.Results.Reader.TryRead(out _).Should().BeFalse();
    }

    private CommandExecutionContext Context() => new(workspace, "model", PermissionMode.WorkspaceWrite, OutputFormat.Text, PrimaryMode.Build);

    /// <summary>The REPL preserves a failed prompt's nonzero exit code when input ends.</summary>
    [Fact]
    public async Task Provider_failure_remains_a_nonzero_exit_at_EOF()
    {
        using var services = Services(new Provider { Fail = true });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        terminal.Input.Writer.TryComplete();
        (await services.GetRequiredService<IReplHost>().RunAsync(Context(), "fail", deadline.Token)).Should().Be(1);
    }

    /// <summary>EOF denies a pending approval and lets the turn settle without a console reader.</summary>
    [Fact]
    public async Task End_of_input_denies_pending_approval_without_hanging()
    {
        var provider = new Provider { AskForShell = true };
        using var services = Services(provider);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var run = services.GetRequiredService<IReplHost>().RunAsync(Context(), "request shell", deadline.Token);
        await terminal.WaitForAsync(message => message.StartsWith("Approval "), deadline.Token);
        terminal.Input.Writer.TryComplete();
        (await run.WaitAsync(deadline.Token)).Should().Be(0);
        var result = await renderer.Results.Reader.ReadAsync(deadline.Token);
        result.ToolResults.Should().ContainSingle().Which.Succeeded.Should().BeFalse();
    }
    private ServiceProvider Services(Provider provider)
    {
        Directory.CreateDirectory(workspace);
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSharpClawCli();
        services.AddSingleton<IReplTerminal>(terminal);
        services.RemoveAll<IOutputRenderer>();
        services.AddSingleton<IOutputRenderer>(renderer);
        services.AddSingleton<IProviderRequestPreflight, Preflight>();
        services.AddSingleton<IAuthFlowService, Auth>();
        services.AddSingleton<IModelProviderResolver>(new Resolver(provider));
        return services.BuildServiceProvider();
    }
    /// <inheritdoc />
    public void Dispose() { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }

    private sealed class Terminal : IReplTerminal
    {
        public Channel<string> Input { get; } = Channel.CreateUnbounded<string>();
        public ConcurrentQueue<string> Messages { get; } = new();
        private readonly Channel<string> output = Channel.CreateUnbounded<string>();
        private int readers;
        public int MaxReaders { get; private set; }
        public event ConsoleCancelEventHandler? CancelKeyPress { add { } remove { } }
        public async ValueTask<string?> ReadLineAsync(string prompt, CancellationToken token)
        {
            MaxReaders = Math.Max(MaxReaders, Interlocked.Increment(ref readers));
            try { return await Input.Reader.WaitToReadAsync(token) ? await Input.Reader.ReadAsync(token) : null; }
            finally { Interlocked.Decrement(ref readers); }
        }
        public void WriteInfo(string message) { Messages.Enqueue(message); output.Writer.TryWrite(message); }
        public void WriteError(string message) => WriteInfo(message);
        public void ClearScreen() { }
        public async Task<string> WaitForAsync(Func<string, bool> predicate, CancellationToken token)
        {
            while (true) { var message = await output.Reader.ReadAsync(token); if (predicate(message)) return message; }
        }
    }
    private sealed class Renderer : IOutputRenderer
    {
        public OutputFormat Format => OutputFormat.Text;
        public Channel<TurnExecutionResult> Results { get; } = Channel.CreateUnbounded<TurnExecutionResult>();
        public Task RenderCommandResultAsync(CommandResult result, CancellationToken token) => Task.CompletedTask;
        public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken token) { Results.Writer.TryWrite(result); return Task.CompletedTask; }
    }
    private sealed class Preflight : IProviderRequestPreflight { public ProviderRequest Prepare(ProviderRequest request) => request with { ProviderName = "repl-test" }; }
    private sealed class Auth : IAuthFlowService { public Task<AuthStatus> GetStatusAsync(string name, CancellationToken token) => Task.FromResult(new AuthStatus("test", true, name, null, null, ["api"])); }
    private sealed class Resolver(IModelProvider provider) : IModelProviderResolver { public IModelProvider Resolve(string name) => provider; }
    private sealed class Provider : IModelProvider
    {
        private int count;
        public bool BlockFirst { get; init; }
        public bool AskForShell { get; init; }
        public bool Fail { get; init; }
        public int Count => Volatile.Read(ref count);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProviderName => "repl-test";
        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken token) => new Auth().GetStatusAsync(ProviderName, token);
        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken token) => Task.FromResult(new ProviderStreamHandle(request, Stream(request, token)));
        private async IAsyncEnumerable<ProviderEvent> Stream(ProviderRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            var index = Interlocked.Increment(ref count);
            Started.TrySetResult();
            if (Fail) throw new IOException("Test provider failure");
            if (index == 1 && BlockFirst) await Task.Delay(Timeout.Infinite, token);
            if (index == 1 && AskForShell)
                yield return new("tool", request.Id, "tool_use", DateTimeOffset.UtcNow, null, false, null, "tool_use", "shell-1", "bash", """{"command":"echo approved"}""");
            else yield return new("text", request.Id, "delta", DateTimeOffset.UtcNow, "Completed", false, null);
            await Task.Yield();
            yield return new("done", request.Id, "done", DateTimeOffset.UtcNow, null, true, new(10, 5, 0, 15, null));
        }
    }
}
