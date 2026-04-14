using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class HookDispatcherTests
{
    [Fact]
    public async Task Hook_dispatcher_should_list_and_test_hooks()
    {
        var processRunner = new RecordingProcessRunner();
        IHookDispatcher dispatcher = new HookDispatcher(
            new FixedConfigService(),
            processRunner,
            NullLogger<HookDispatcher>.Instance);

        var listed = await dispatcher.ListAsync("/workspace", CancellationToken.None);
        var test = await dispatcher.TestAsync("/workspace", "post-turn", "{\"kind\":\"test\"}", CancellationToken.None);
        var listedAfterTest = await dispatcher.ListAsync("/workspace", CancellationToken.None);

        listed.Should().ContainSingle(item => item.Name == "post-turn");
        test.Succeeded.Should().BeTrue();
        processRunner.Requests.Should().ContainSingle();
        listedAfterTest.Single().LastTestSucceeded.Should().BeTrue();
    }

    private sealed class FixedConfigService : ISharpClawConfigService
    {
        public Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new SharpClawConfigSnapshot(
                workspaceRoot,
                null,
                null,
                new SharpClawConfigDocument(
                    ShareMode.Manual,
                    null,
                    null,
                    null,
                    null,
                    [new HookDefinition("post-turn", HookTriggerKind.TurnCompleted, "echo", ["ok"])],
                    null)));
    }

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "ok", string.Empty, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
    }
}
