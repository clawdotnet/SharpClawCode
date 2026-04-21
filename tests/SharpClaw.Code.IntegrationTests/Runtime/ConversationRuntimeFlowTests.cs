using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies the first durable conversation runtime flow.
/// </summary>
public sealed class ConversationRuntimeFlowTests
{
    /// <summary>
    /// Ensures running a prompt creates a durable session and append-only event log.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_create_session_snapshot_event_log_and_checkpoint()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "inspect the runtime state",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Json,
                Metadata: null),
            CancellationToken.None);

        result.Session.Id.Should().NotBeNullOrWhiteSpace();
        result.Turn.SequenceNumber.Should().Be(1);
        result.Events.Should().NotBeEmpty();
        result.Checkpoint.Should().NotBeNull();

        var sessionRoot = Path.Combine(workspacePath, ".sharpclaw", "sessions", result.Session.Id);
        File.Exists(Path.Combine(sessionRoot, "session.json")).Should().BeTrue();
        File.Exists(Path.Combine(sessionRoot, "events.ndjson")).Should().BeTrue();
        File.Exists(Path.Combine(sessionRoot, "checkpoints", $"{result.Checkpoint!.Id}.json")).Should().BeTrue();
        File.ReadAllLines(Path.Combine(sessionRoot, "events.ndjson")).Length.Should().BeGreaterThanOrEqualTo(4);
    }

    /// <summary>
    /// Ensures running another prompt without a session id resumes the latest session.
    /// </summary>
    [Fact]
    public async Task RunPrompt_without_session_id_should_resume_latest_session()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var first = await runtime.RunPromptAsync(
            new RunPromptRequest("first prompt", null, workspacePath, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);

        var second = await runtime.RunPromptAsync(
            new RunPromptRequest("second prompt", null, workspacePath, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);

        second.Session.Id.Should().Be(first.Session.Id);
        second.Turn.SequenceNumber.Should().Be(2);
        second.Session.UpdatedAtUtc.Should().BeAfter(first.Session.UpdatedAtUtc);
    }

    /// <summary>
    /// Ensures an explicit session id resumes that session and latest resolution reflects it.
    /// </summary>
    [Fact]
    public async Task RunPrompt_with_session_id_should_resume_requested_session()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var created = await runtime.RunPromptAsync(
            new RunPromptRequest("seed prompt", null, workspacePath, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);

        var resumed = await runtime.RunPromptAsync(
            new RunPromptRequest("resume prompt", created.Session.Id, workspacePath, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);

        var latestSession = await runtime.GetLatestSessionAsync(workspacePath, CancellationToken.None);

        resumed.Session.Id.Should().Be(created.Session.Id);
        resumed.Turn.SequenceNumber.Should().Be(2);
        latestSession.Should().NotBeNull();
        latestSession!.Id.Should().Be(created.Session.Id);
    }

    /// <summary>
    /// Ensures canceled turns are durably marked failed with a stable reason for recovery paths.
    /// </summary>
    [Fact]
    public async Task RunPrompt_cancellation_should_persist_failed_session_state()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var request = new RunPromptRequest(
            Prompt: "cancel me",
            SessionId: null,
            WorkingDirectory: workspacePath,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            Metadata: new Dictionary<string, string>
            {
                ["provider"] = DeterministicMockModelProvider.ProviderNameConstant,
                ["model"] = DeterministicMockModelProvider.DefaultModelId,
                [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamSlow,
            });
        var act = async () => await RunPromptWithCancelAfterTurnStartAsync(runtime, request, workspacePath, TimeSpan.FromMilliseconds(400));

        await act.Should().ThrowAsync<OperationCanceledException>();
        var latestSession = await runtime.GetLatestSessionAsync(workspacePath, CancellationToken.None);
        latestSession.Should().NotBeNull();
        latestSession!.State.Should().Be(SessionLifecycleState.Failed);

        var eventLogPath = Path.Combine(workspacePath, ".sharpclaw", "sessions", latestSession.Id, "events.ndjson");
        File.ReadAllText(eventLogPath).Should().Contain("The turn was canceled.");
    }

    /// <summary>
    /// After a failed turn (e.g. cancellation), a subsequent prompt should recover lifecycle and run successfully.
    /// </summary>
    [Fact]
    public async Task RunPrompt_after_failed_turn_should_recover_and_complete_next_turn()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var cancelRequest = new RunPromptRequest(
            Prompt: "cancel me",
            SessionId: null,
            WorkingDirectory: workspacePath,
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Text,
            Metadata: new Dictionary<string, string>
            {
                ["provider"] = DeterministicMockModelProvider.ProviderNameConstant,
                ["model"] = DeterministicMockModelProvider.DefaultModelId,
                [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamSlow,
            });
        var cancelAct = async () => await RunPromptWithCancelAfterTurnStartAsync(runtime, cancelRequest, workspacePath, TimeSpan.FromMilliseconds(400));
        await cancelAct.Should().ThrowAsync<OperationCanceledException>();

        var second = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "ok now",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = DeterministicMockModelProvider.ProviderNameConstant,
                    ["model"] = DeterministicMockModelProvider.DefaultModelId,
                }),
            CancellationToken.None);

        second.Session.State.Should().Be(SessionLifecycleState.Active);
        second.Turn.SequenceNumber.Should().Be(2);
    }

    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddDeterministicMockModelProvider();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Starts <see cref="IConversationRuntime.RunPromptAsync"/> and waits for an active turn before arming cancellation.
    /// This keeps the test focused on recovery from an in-flight turn rather than cancellation during setup.
    /// </summary>
    private static async Task RunPromptWithCancelAfterTurnStartAsync(
        IConversationRuntime runtime,
        RunPromptRequest request,
        string workspacePath,
        TimeSpan cancelAfter)
    {
        using var cts = new CancellationTokenSource();
        var runTask = runtime.RunPromptAsync(request, cts.Token);
        await WaitForActiveTurnAsync(runtime, workspacePath, CancellationToken.None).ConfigureAwait(false);
        cts.CancelAfter(cancelAfter);
        await runTask.ConfigureAwait(false);
    }

    private static async Task WaitForActiveTurnAsync(
        IConversationRuntime runtime,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var latestSession = await runtime.GetLatestSessionAsync(workspacePath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(latestSession?.ActiveTurnId))
            {
                return;
            }

            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("The runtime did not activate a turn before cancellation was requested.");
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }
}
