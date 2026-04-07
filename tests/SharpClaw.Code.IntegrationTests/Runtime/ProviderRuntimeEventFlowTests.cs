using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies provider activity is surfaced through the runtime flow.
/// </summary>
public sealed class ProviderRuntimeEventFlowTests
{
    private static readonly DateTimeOffset BaseTimestampUtc = new(2026, 4, 6, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Ensures streamed provider activity becomes part of the durable runtime event sequence.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_include_provider_runtime_events_when_provider_streams()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
        services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
        services.AddSingleton<IModelProviderResolver, StubModelProviderResolver>();
        using var serviceProvider = services.BuildServiceProvider();

        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "stream something useful",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "stub-provider",
                    ["model"] = "stub-model"
                }),
            CancellationToken.None);

        result.FinalOutput.Should().Be("Hello world");
        result.Events.Should().ContainSingle(runtimeEvent => runtimeEvent is ProviderStartedEvent);
        result.Events.OfType<ProviderDeltaEvent>().Select(providerDeltaEvent => providerDeltaEvent.Content).Should().ContainInOrder("Hello", " world");
        result.Events.OfType<ProviderCompletedEvent>().Should().ContainSingle(providerCompletedEvent => providerCompletedEvent.ProviderName == "stub-provider");
    }

    /// <summary>
    /// Ensures a missing provider fails the turn with a classified provider exception.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_fail_when_provider_is_missing()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, MissingProviderAuthFlowService>();
        });
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var sessionStore = serviceProvider.GetRequiredService<ISessionStore>();

        var act = async () => await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "missing provider",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "missing-provider",
                    ["model"] = "stub-model"
                }),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ProviderExecutionException>();
        exception.Which.Kind.Should().Be(ProviderFailureKind.MissingProvider);

        var latestSession = await sessionStore.GetLatestAsync(workspacePath, CancellationToken.None);
        latestSession.Should().NotBeNull();
        latestSession!.State.Should().Be(SessionLifecycleState.Failed);
    }

    /// <summary>
    /// Ensures provider stream failures fail the turn with a classified provider exception.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_fail_when_provider_stream_fails()
    {
        var workspacePath = CreateTemporaryWorkspace();
        using var serviceProvider = CreateRuntimeServices(services =>
        {
            services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
            services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
            services.AddSingleton<IModelProviderResolver, ThrowingModelProviderResolver>();
        });
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var sessionStore = serviceProvider.GetRequiredService<ISessionStore>();

        var act = async () => await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "provider failure",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "stub-provider",
                    ["model"] = "stub-model"
                }),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<ProviderExecutionException>();
        exception.Which.Kind.Should().Be(ProviderFailureKind.StreamFailed);

        var latestSession = await sessionStore.GetLatestAsync(workspacePath, CancellationToken.None);
        latestSession.Should().NotBeNull();
        latestSession!.State.Should().Be(SessionLifecycleState.Failed);
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-provider-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private static ServiceProvider CreateRuntimeServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        configure(services);
        return services.BuildServiceProvider();
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("stub-subject", true, providerName, null, null, ["api"]));
    }

    private sealed class MissingProviderAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => throw new InvalidOperationException($"Provider '{providerName}' is not registered.");
    }

    private sealed class StubModelProviderResolver : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => new StubModelProvider();
    }

    private sealed class ThrowingModelProviderResolver : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => new ThrowingModelProvider();
    }

    private sealed class StubModelProvider : IModelProvider
    {
        public string ProviderName => "stub-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("stub-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request)));

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(ProviderRequest request)
        {
            yield return new ProviderEvent("provider-event-1", request.Id, "delta", BaseTimestampUtc.AddMilliseconds(1), "Hello", false, null);
            await Task.Yield();
            yield return new ProviderEvent("provider-event-2", request.Id, "delta", BaseTimestampUtc.AddMilliseconds(2), " world", false, null);
            await Task.Yield();
            yield return new ProviderEvent("provider-event-3", request.Id, "completed", BaseTimestampUtc.AddMilliseconds(3), null, true, new UsageSnapshot(1, 2, 0, 3, null));
        }
    }

    private sealed class ThrowingModelProvider : IModelProvider
    {
        public string ProviderName => "stub-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("stub-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ProviderStreamHandle(request, ThrowAsync()));

        private static async IAsyncEnumerable<ProviderEvent> ThrowAsync()
        {
            await Task.Yield();
            throw new InvalidOperationException("boom");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }
}
