using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies prompt-reference approval behavior respects the normalized runtime interactivity flag.
/// </summary>
public sealed class PromptInteractivityFlowTests
{
    /// <summary>
    /// Ensures interactive runtime callers can approve outside-workspace prompt references.
    /// </summary>
    [Fact]
    public async Task ExecutePromptAsync_should_allow_outside_workspace_reference_for_interactive_callers()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var outsideFile = CreateOutsideWorkspaceFile("interactive content");
        var provider = new CapturingPromptProvider();
        using var serviceProvider = CreateServiceProvider(provider, new ApprovingApprovalService());
        var runtime = serviceProvider.GetRequiredService<IRuntimeCommandService>();

        var result = await runtime.ExecutePromptAsync(
            $"inspect @{outsideFile}",
            new RuntimeCommandContext(
                WorkingDirectory: workspacePath,
                Model: "prompt-model",
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                IsInteractive: true),
            CancellationToken.None);

        var providerStarted = result.Events.OfType<ProviderStartedEvent>().Single();
        providerStarted.Request.Prompt.Should().Contain("interactive content");
        provider.CapturedRequests.Should().ContainSingle();
    }

    /// <summary>
    /// Ensures non-interactive runtime callers cannot approve outside-workspace prompt references.
    /// </summary>
    [Fact]
    public async Task ExecutePromptAsync_should_deny_outside_workspace_reference_for_non_interactive_callers()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var outsideFile = CreateOutsideWorkspaceFile("non-interactive content");
        using var serviceProvider = CreateServiceProvider(new CapturingPromptProvider());
        var runtime = serviceProvider.GetRequiredService<IRuntimeCommandService>();

        var act = async () => await runtime.ExecutePromptAsync(
            $"inspect @{outsideFile}",
            new RuntimeCommandContext(
                WorkingDirectory: workspacePath,
                Model: "prompt-model",
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                IsInteractive: false),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*non-interactive*");
    }

    private static ServiceProvider CreateServiceProvider(CapturingPromptProvider provider, IApprovalService? approvalService = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IProviderRequestPreflight, DefaultingPreflight>();
        services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
        services.AddSingleton<IModelProviderResolver>(_ => new StaticModelProviderResolver(provider));
        if (approvalService is not null)
        {
            services.AddSingleton(approvalService);
            services.AddSingleton<IApprovalService>(approvalService);
        }

        return services.BuildServiceProvider();
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-interactivity-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private static string CreateOutsideWorkspaceFile(string content)
    {
        var outsideRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-interactivity-targets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        var path = Path.Combine(outsideRoot, "note.txt");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class DefaultingPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request)
            => string.IsNullOrWhiteSpace(request.ProviderName)
                ? request with { ProviderName = "prompt-provider" }
                : request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("prompt-subject", true, providerName, null, null, ["api"]));
    }

    private sealed class StaticModelProviderResolver(IModelProvider provider) : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => provider;
    }

    private sealed class CapturingPromptProvider : IModelProvider
    {
        public List<ProviderRequest> CapturedRequests { get; } = [];

        public string ProviderName => "prompt-provider";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("prompt-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            return Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request, cancellationToken)));
        }

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
            ProviderRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new ProviderEvent("provider-event-1", request.Id, "delta", DateTimeOffset.UtcNow, "ok", false, null);
            await Task.Yield();
            yield return new ProviderEvent("provider-event-2", request.Id, "completed", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
        }
    }

    private sealed class ApprovingApprovalService : IApprovalService
    {
        public Task<ApprovalDecision> RequestApprovalAsync(
            ApprovalRequest request,
            PermissionEvaluationContext context,
            CancellationToken cancellationToken)
            => Task.FromResult(new ApprovalDecision(
                request.Scope,
                true,
                request.RequestedBy,
                "test",
                "approved",
                DateTimeOffset.UtcNow,
                null));
    }
}
