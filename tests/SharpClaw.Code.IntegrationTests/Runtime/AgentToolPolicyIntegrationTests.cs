using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies agent tool advertisement and approval behavior through the runtime.
/// </summary>
public sealed class AgentToolPolicyIntegrationTests
{
    /// <summary>
    /// Ensures the provider only sees tools from the resolved explicit allow list.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_only_advertise_tools_from_explicit_allow_list()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var provider = new CapturingToolPolicyProvider(ToolPolicyScenario.CaptureOnly);
        using var serviceProvider = CreateServiceProvider(provider);
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "list available tools",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = TestProviderName,
                    ["model"] = "tool-policy-model",
                    [SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = """["read_file"]""",
                    [ScenarioMetadataKey] = ToolPolicyScenario.CaptureOnly,
                }),
            CancellationToken.None);

        provider.CapturedRequests.Should().NotBeEmpty();
        provider.CapturedRequests[0].Tools.Should().NotBeNull();
        provider.CapturedRequests[0].Tools!.Select(static tool => tool.Name).Should().Equal("read_file");
    }

    /// <summary>
    /// Ensures plugin-backed tools are filtered through the same explicit allow list as built-ins.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_filter_plugin_tools_through_same_allow_list_path()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var provider = new CapturingToolPolicyProvider(ToolPolicyScenario.CaptureOnly);
        using var serviceProvider = CreateServiceProvider(
            provider,
            pluginManager: new StubPluginManager([
                new PluginToolDescriptor("plugin_echo", "Echo via plugin.", "Plugin payload.", ["plugin"]),
            ]));
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "list available tools",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = TestProviderName,
                    ["model"] = "tool-policy-model",
                    [SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = """["plugin_echo"]""",
                    [ScenarioMetadataKey] = ToolPolicyScenario.CaptureOnly,
                }),
            CancellationToken.None);

        provider.CapturedRequests.Should().NotBeEmpty();
        provider.CapturedRequests[0].Tools.Should().NotBeNull();
        provider.CapturedRequests[0].Tools!.Select(static tool => tool.Name).Should().Equal("plugin_echo");
    }

    /// <summary>
    /// Ensures interactive agent tool calls can request approval and reach the shell executor when approved.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_allow_interactive_agent_tool_execution_to_request_approval()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var provider = new CapturingToolPolicyProvider(ToolPolicyScenario.ToolRoundTrip);
        var approvalService = new RecordingApprovalService(approve: true);
        var shellExecutor = new RecordingShellExecutor();
        using var serviceProvider = CreateServiceProvider(provider, approvalService, shellExecutor);
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "run bash",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = TestProviderName,
                    ["model"] = "tool-policy-model",
                    [SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = """["bash"]""",
                    [ScenarioMetadataKey] = ToolPolicyScenario.ToolRoundTrip,
                },
                IsInteractive: true),
            CancellationToken.None);

        result.FinalOutput.Should().Contain("Tool result received");
        approvalService.Requests.Should().ContainSingle();
        approvalService.Requests[0].Context.IsInteractive.Should().BeTrue();
        shellExecutor.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Ensures non-interactive agent tool calls remain denied before any shell execution occurs.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_deny_non_interactive_agent_tool_execution_before_shell_runs()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var provider = new CapturingToolPolicyProvider(ToolPolicyScenario.ToolRoundTrip);
        var approvalService = new RecordingApprovalService(approve: true);
        var shellExecutor = new RecordingShellExecutor();
        using var serviceProvider = CreateServiceProvider(provider, approvalService, shellExecutor);
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "run bash",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = TestProviderName,
                    ["model"] = "tool-policy-model",
                    [SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = """["bash"]""",
                    [ScenarioMetadataKey] = ToolPolicyScenario.ToolRoundTrip,
                },
                IsInteractive: false),
            CancellationToken.None);

        result.FinalOutput.Should().Contain("Tool result received");
        approvalService.Requests.Should().ContainSingle();
        approvalService.Requests[0].Context.IsInteractive.Should().BeFalse();
        shellExecutor.CallCount.Should().Be(0);
    }

    /// <summary>
    /// Ensures a tool requested outside the explicit allow list is denied even if the provider emits it.
    /// </summary>
    [Fact]
    public async Task RunPrompt_should_deny_provider_requested_tool_that_is_not_in_allow_list()
    {
        var workspacePath = CreateTemporaryWorkspace();
        var provider = new CapturingToolPolicyProvider(ToolPolicyScenario.ToolRoundTrip);
        var approvalService = new RecordingApprovalService(approve: true);
        var shellExecutor = new RecordingShellExecutor();
        using var serviceProvider = CreateServiceProvider(provider, approvalService, shellExecutor);
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();

        var result = await runtime.RunPromptAsync(
            new RunPromptRequest(
                Prompt: "run bash",
                SessionId: null,
                WorkingDirectory: workspacePath,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = TestProviderName,
                    ["model"] = "tool-policy-model",
                    [SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = """["read_file"]""",
                    [ScenarioMetadataKey] = ToolPolicyScenario.ToolRoundTrip,
                },
                IsInteractive: true),
            CancellationToken.None);

        result.FinalOutput.Should().Contain("Tool result received");
        result.ToolResults.Should().ContainSingle();
        result.ToolResults[0].Succeeded.Should().BeFalse();
        result.ToolResults[0].ErrorMessage.Should().Contain("allow list");
        approvalService.Requests.Should().BeEmpty();
        shellExecutor.CallCount.Should().Be(0);
    }

    private static ServiceProvider CreateServiceProvider(
        CapturingToolPolicyProvider provider,
        RecordingApprovalService? approvalService = null,
        RecordingShellExecutor? shellExecutor = null,
        IPluginManager? pluginManager = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IProviderRequestPreflight, PassthroughPreflight>();
        services.AddSingleton<IAuthFlowService, AlwaysAuthenticatedAuthFlowService>();
        services.AddSingleton<IModelProviderResolver>(_ => new StaticModelProviderResolver(provider));
        if (approvalService is not null)
        {
            services.AddSingleton<IApprovalService>(approvalService);
        }

        if (shellExecutor is not null)
        {
            services.AddSingleton<IShellExecutor>(shellExecutor);
        }

        if (pluginManager is not null)
        {
            services.AddSingleton(pluginManager);
            services.AddSingleton<IPluginManager>(pluginManager);
        }

        return services.BuildServiceProvider();
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-agent-tool-policy-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }

    private const string TestProviderName = "tool-policy-provider";
    private const string ScenarioMetadataKey = "scenario";

    private static class ToolPolicyScenario
    {
        public const string CaptureOnly = "capture-only";
        public const string ToolRoundTrip = "tool-round-trip";
    }

    private sealed class PassthroughPreflight : IProviderRequestPreflight
    {
        public ProviderRequest Prepare(ProviderRequest request) => request;
    }

    private sealed class AlwaysAuthenticatedAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("tool-policy-subject", true, providerName, null, null, ["api"]));
    }

    private sealed class StaticModelProviderResolver(IModelProvider provider) : IModelProviderResolver
    {
        public IModelProvider Resolve(string providerName) => provider;
    }

    private sealed class CapturingToolPolicyProvider(string defaultScenario) : IModelProvider
    {
        public List<ProviderRequest> CapturedRequests { get; } = [];

        public string ProviderName => TestProviderName;

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus("tool-policy-subject", true, ProviderName, null, null, ["api"]));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
        {
            CapturedRequests.Add(request);
            return Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request, ResolveScenario(request), cancellationToken)));
        }

        private string ResolveScenario(ProviderRequest request)
            => request.Metadata is not null
                && request.Metadata.TryGetValue(ScenarioMetadataKey, out var scenario)
                && !string.IsNullOrWhiteSpace(scenario)
                    ? scenario
                    : defaultScenario;

        private static async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
            ProviderRequest request,
            string scenario,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = cancellationToken;

            if (string.Equals(scenario, ToolPolicyScenario.ToolRoundTrip, StringComparison.Ordinal))
            {
                if (HasToolResult(request))
                {
                    yield return new ProviderEvent("provider-event-2", request.Id, "delta", DateTimeOffset.UtcNow, "Tool result received", false, null);
                    yield return new ProviderEvent("provider-event-3", request.Id, "completed", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
                    yield break;
                }

                yield return new ProviderEvent(
                    "provider-event-1",
                    request.Id,
                    "tool_use",
                    DateTimeOffset.UtcNow,
                    null,
                    false,
                    null,
                    BlockType: "tool_use",
                    ToolUseId: "toolu_bash_001",
                    ToolName: "bash",
                    ToolInputJson: """{"command":"echo hi"}""");
                yield return new ProviderEvent("provider-event-1-terminal", request.Id, "completed", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
                yield break;
            }

            yield return new ProviderEvent("provider-event-1", request.Id, "delta", DateTimeOffset.UtcNow, "ok", false, null);
            await Task.Yield();
            yield return new ProviderEvent("provider-event-2", request.Id, "completed", DateTimeOffset.UtcNow, null, true, new UsageSnapshot(1, 2, 0, 3, null));
        }

        private static bool HasToolResult(ProviderRequest request)
            => request.Messages is not null
               && request.Messages.SelectMany(static message => message.Content)
                   .Any(static block => block.Kind == ContentBlockKind.ToolResult);
    }

    private sealed class RecordingApprovalService(bool approve) : IApprovalService
    {
        public List<(ApprovalRequest Request, PermissionEvaluationContext Context)> Requests { get; } = [];

        public Task<ApprovalDecision> RequestApprovalAsync(
            ApprovalRequest request,
            PermissionEvaluationContext context,
            CancellationToken cancellationToken)
        {
            Requests.Add((request, context));
            return Task.FromResult(new ApprovalDecision(
                request.Scope,
                approve && context.IsInteractive,
                request.RequestedBy,
                "test",
                approve && context.IsInteractive ? "approved" : "denied",
                DateTimeOffset.UtcNow,
                null));
        }
    }

    private sealed class RecordingShellExecutor : IShellExecutor
    {
        public int CallCount { get; private set; }

        public Task<ProcessRunResult> ExecuteAsync(
            string command,
            string? workingDirectory,
            IReadOnlyDictionary<string, string?>? environmentVariables,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessRunResult(0, "hi", string.Empty, now, now));
        }
    }

    private sealed class StubPluginManager(IReadOnlyList<PluginToolDescriptor> descriptors) : IPluginManager
    {
        public Task<IReadOnlyList<LoadedPlugin>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<LoadedPlugin>>([]);

        public Task<LoadedPlugin> InstallAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> EnableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> DisableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task UninstallAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<LoadedPlugin> UpdateAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PluginToolDescriptor>> ListToolDescriptorsAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(descriptors);

        public Task<ToolResult> ExecuteToolAsync(string workspaceRoot, string toolName, ToolExecutionRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ToolResult(request.Id, toolName, true, OutputFormat.Text, "plugin", null, 0, null, null));
    }
}
