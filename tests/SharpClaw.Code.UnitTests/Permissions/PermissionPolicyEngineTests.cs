using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Permissions.Rules;
using SharpClaw.Code.Permissions.Services;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Permissions;

/// <summary>
/// Verifies ordered permission rule evaluation and approval behavior.
/// </summary>
public sealed class PermissionPolicyEngineTests
{
    /// <summary>
    /// Ensures the allowed-tool rule blocks disallowed tools before broader mode allowances apply.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_deny_tool_not_in_explicit_allow_list()
    {
        var approvalService = new StubApprovalService();
        var engine = CreateEngine(approvalService);
        var context = CreateContext(
            permissionMode: PermissionMode.DangerFullAccess,
            allowedTools: [ "read_file" ]);
        var request = CreateRequest("write_file", "{}", ApprovalScope.FileSystemWrite, requiresApproval: true, isDestructive: true);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("allow list");
        approvalService.RequestCount.Should().Be(0);
    }

    /// <summary>
    /// Ensures the workspace boundary rule denies escaping file paths before execution starts.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_deny_request_that_escapes_workspace()
    {
        var engine = CreateEngine(new StubApprovalService());
        var context = CreateContext(permissionMode: PermissionMode.DangerFullAccess);
        var request = CreateRequest(
            "write_file",
            """{"path":"../escape.txt","content":"nope"}""",
            ApprovalScope.FileSystemWrite,
            requiresApproval: true,
            isDestructive: true);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("workspace");
    }

    /// <summary>
    /// Ensures approval decisions can be remembered per session when a trust rule requires approval.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_remember_session_scoped_plugin_approval()
    {
        var approvalService = new StubApprovalService();
        var engine = CreateEngine(approvalService);
        var context = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            sourceKind: PermissionRequestSourceKind.Plugin,
            sourceName: "sample-plugin",
            trustedPlugins: []);
        var request = CreateRequest("read_file", """{"path":"notes.txt","offset":null,"limit":null}""", ApprovalScope.ToolExecution, requiresApproval: false, isDestructive: false);

        var firstDecision = await engine.EvaluateAsync(request, context, CancellationToken.None);
        var secondDecision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        firstDecision.IsAllowed.Should().BeTrue();
        secondDecision.IsAllowed.Should().BeTrue();
        approvalService.RequestCount.Should().Be(1);
    }

    /// <summary>
    /// Ensures remembered approvals for plugin-surfaced tools are not reused across different originating plugins.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_not_reuse_remembered_approval_across_distinct_plugin_origins_for_same_tool()
    {
        var approvalService = new StubApprovalService();
        var engine = CreateEngine(approvalService);
        var request = CreateRequest(
            "read_file",
            """{"path":"notes.txt","offset":null,"limit":null}""",
            ApprovalScope.ToolExecution,
            requiresApproval: false,
            isDestructive: false);

        var contextPluginA = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            toolOriginatingPluginId: "plugin-a",
            toolOriginatingPluginTrust: PluginTrustLevel.Untrusted,
            trustedPlugins: []);

        var contextPluginB = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            toolOriginatingPluginId: "plugin-b",
            toolOriginatingPluginTrust: PluginTrustLevel.Untrusted,
            trustedPlugins: []);

        var firstFromA = await engine.EvaluateAsync(request, contextPluginA, CancellationToken.None);
        var secondFromA = await engine.EvaluateAsync(request, contextPluginA, CancellationToken.None);
        var firstFromB = await engine.EvaluateAsync(request, contextPluginB, CancellationToken.None);

        firstFromA.IsAllowed.Should().BeTrue();
        secondFromA.IsAllowed.Should().BeTrue();
        firstFromB.IsAllowed.Should().BeTrue();
        approvalService.RequestCount.Should().Be(2);
    }

    /// <summary>
    /// Ensures dangerous shell patterns are denied for non-interactive automation unless explicitly overridden.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_deny_dangerous_shell_pattern_in_non_interactive_mode()
    {
        var engine = CreateEngine(new NonInteractiveApprovalService());
        var context = CreateContext(
            permissionMode: PermissionMode.DangerFullAccess,
            isInteractive: false,
            allowDangerousOverride: false);
        var request = CreateRequest(
            "bash",
            """{"command":"rm -rf .","workingDirectory":".","environmentVariables":null}""",
            ApprovalScope.ShellExecution,
            requiresApproval: true,
            isDestructive: true);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("dangerous");
    }

    /// <summary>
    /// Ensures a plugin-surfaced tool with manifest <see cref="PluginTrustLevel.Untrusted"/> requires approval,
    /// which fails when the session is non-interactive.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_deny_untrusted_plugin_surfaced_tool_when_non_interactive()
    {
        var engine = CreateEngine(new NonInteractiveApprovalService());
        var context = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            isInteractive: false,
            toolOriginatingPluginId: "risky-plugin",
            toolOriginatingPluginTrust: PluginTrustLevel.Untrusted,
            trustedPlugins: []);
        var request = CreateRequest(
            "read_file",
            """{"path":"notes.txt","offset":null,"limit":null}""",
            ApprovalScope.ToolExecution,
            requiresApproval: false,
            isDestructive: false);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("non-interactive");
    }

    /// <summary>
    /// Ensures manifest-trusted plugin tools do not add a plugin-trust approval gate.
    /// </summary>
    [Theory]
    [InlineData(PluginTrustLevel.WorkspaceTrusted)]
    [InlineData(PluginTrustLevel.DeveloperTrusted)]
    public async Task EvaluateAsync_should_allow_manifest_trusted_plugin_surfaced_tool_without_approval(PluginTrustLevel manifestTrust)
    {
        var engine = CreateEngine(new NonInteractiveApprovalService());
        var context = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            isInteractive: false,
            toolOriginatingPluginId: "safe-plugin",
            toolOriginatingPluginTrust: manifestTrust,
            trustedPlugins: []);
        var request = CreateRequest(
            "read_file",
            """{"path":"notes.txt","offset":null,"limit":null}""",
            ApprovalScope.ToolExecution,
            requiresApproval: false,
            isDestructive: false);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeTrue();
    }

    /// <summary>
    /// Ensures an untrusted manifest tier is waived when the originating plugin id is session-trusted.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_allow_untrusted_manifest_plugin_tool_when_plugin_is_session_trusted()
    {
        var engine = CreateEngine(new NonInteractiveApprovalService());
        var context = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            isInteractive: false,
            toolOriginatingPluginId: "my-plugin",
            toolOriginatingPluginTrust: PluginTrustLevel.Untrusted,
            trustedPlugins: [ "my-plugin" ]);
        var request = CreateRequest(
            "read_file",
            """{"path":"notes.txt","offset":null,"limit":null}""",
            ApprovalScope.ToolExecution,
            requiresApproval: false,
            isDestructive: false);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeTrue();
    }

    /// <summary>
    /// Ensures trusted MCP callers can proceed without approval when the mode otherwise allows execution.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_should_allow_trusted_mcp_request()
    {
        var approvalService = new StubApprovalService();
        var engine = CreateEngine(approvalService);
        var context = CreateContext(
            permissionMode: PermissionMode.WorkspaceWrite,
            sourceKind: PermissionRequestSourceKind.Mcp,
            sourceName: "filesystem",
            trustedMcpServers: [ "filesystem" ]);
        var request = CreateRequest("read_file", """{"path":"notes.txt","offset":null,"limit":null}""", ApprovalScope.ToolExecution, requiresApproval: false, isDestructive: false);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeTrue();
        approvalService.RequestCount.Should().Be(0);
    }

    /// <summary>
    /// Ensures plan primary mode denies workspace writes even when permission mode would allow them.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_plan_primary_mode_denies_file_writes()
    {
        var engine = CreateEngine(new StubApprovalService());
        var context = CreateContext(PermissionMode.WorkspaceWrite, primaryMode: PrimaryMode.Plan);
        var request = CreateRequest(
            "write_file",
            """{"path":"notes.txt","content":"x"}""",
            ApprovalScope.FileSystemWrite,
            requiresApproval: true,
            isDestructive: true);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeFalse();
        decision.Reason.Should().Contain("Plan mode");
    }

    /// <summary>
    /// Ensures spec primary mode preserves build-mode mutation behavior.
    /// </summary>
    [Fact]
    public async Task EvaluateAsync_spec_primary_mode_allows_workspace_writes()
    {
        var engine = CreateEngine(new StubApprovalService());
        var context = CreateContext(PermissionMode.WorkspaceWrite, primaryMode: PrimaryMode.Spec);
        var request = CreateRequest(
            "write_file",
            """{"path":"notes.txt","content":"x"}""",
            ApprovalScope.FileSystemWrite,
            requiresApproval: true,
            isDestructive: true);

        var decision = await engine.EvaluateAsync(request, context, CancellationToken.None);

        decision.IsAllowed.Should().BeTrue();
    }

    private static IPermissionPolicyEngine CreateEngine(IApprovalService approvalService)
        => new PermissionPolicyEngine(
            [
                new WorkspaceBoundaryRule(new PathService()),
                new PrimaryModeMutationRule(),
                new AllowedToolRule(),
                new DangerousShellPatternRule(),
                new PluginTrustRule(),
                new McpTrustRule()
            ],
            approvalService,
            new SessionApprovalMemory());

    private static PermissionEvaluationContext CreateContext(
        PermissionMode permissionMode,
        string[]? allowedTools = null,
        bool isInteractive = true,
        bool allowDangerousOverride = false,
        PermissionRequestSourceKind sourceKind = PermissionRequestSourceKind.Runtime,
        string? sourceName = null,
        string[]? trustedPlugins = null,
        string[]? trustedMcpServers = null,
        string? toolOriginatingPluginId = null,
        PluginTrustLevel? toolOriginatingPluginTrust = null,
        PrimaryMode primaryMode = PrimaryMode.Build)
        => new(
            SessionId: "session-001",
            WorkspaceRoot: "/workspace",
            WorkingDirectory: "/workspace",
            PermissionMode: permissionMode,
            AllowedTools: allowedTools,
            AllowDangerousBypass: allowDangerousOverride,
            IsInteractive: isInteractive,
            SourceKind: sourceKind,
            SourceName: sourceName,
            TrustedPluginNames: trustedPlugins,
            TrustedMcpServerNames: trustedMcpServers,
            ToolOriginatingPluginId: toolOriginatingPluginId,
            ToolOriginatingPluginTrust: toolOriginatingPluginTrust,
            PrimaryMode: primaryMode);

    private static ToolExecutionRequest CreateRequest(
        string toolName,
        string argumentsJson,
        ApprovalScope approvalScope,
        bool requiresApproval,
        bool isDestructive)
        => new(
            Id: $"tool-request-{Guid.NewGuid():N}",
            SessionId: "session-001",
            TurnId: "turn-001",
            ToolName: toolName,
            ArgumentsJson: argumentsJson,
            ApprovalScope: approvalScope,
            WorkingDirectory: "/workspace",
            RequiresApproval: requiresApproval,
            IsDestructive: isDestructive);

    private sealed class StubApprovalService : IApprovalService
    {
        public int RequestCount { get; private set; }

        public Task<ApprovalDecision> RequestApprovalAsync(
            ApprovalRequest request,
            PermissionEvaluationContext context,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new ApprovalDecision(
                Scope: request.Scope,
                Approved: true,
                RequestedBy: request.RequestedBy,
                ResolvedBy: "test",
                Reason: "approved",
                ResolvedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(10),
                RememberForSession: request.CanRememberDecision));
        }
    }
}
