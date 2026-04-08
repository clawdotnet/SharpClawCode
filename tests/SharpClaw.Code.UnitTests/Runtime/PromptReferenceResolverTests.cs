using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Permissions.Rules;
using SharpClaw.Code.Permissions.Services;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Prompts;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies prompt file references enforce canonical workspace boundaries.
/// </summary>
public sealed class PromptReferenceResolverTests
{
    /// <summary>
    /// Ensures a symlinked prompt reference escaping the workspace is denied.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_should_reject_symlinked_reference_outside_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-ref-tests", Guid.NewGuid().ToString("N"));
        var outsideRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-ref-targets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outsideRoot);
        var outsideFile = Path.Combine(outsideRoot, "secret.txt");
        await File.WriteAllTextAsync(outsideFile, "secret");
        var linkedFile = Path.Combine(workspace, "linked.txt");

        try
        {
            File.CreateSymbolicLink(linkedFile, outsideFile);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        var pathService = new PathService();
        var engine = new PermissionPolicyEngine(
            [
                new WorkspaceBoundaryRule(pathService),
                new PrimaryModeMutationRule(),
                new AllowedToolRule(),
                new DangerousShellPatternRule(),
                new PluginTrustRule(),
                new McpTrustRule()
            ],
            new NonInteractiveApprovalService(),
            new SessionApprovalMemory());
        var resolver = new PromptReferenceResolver(new LocalFileSystem(), pathService, engine);
        var session = new ConversationSession(
            "session-001",
            "Session",
            SessionLifecycleState.Active,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Text,
            workspace,
            workspace,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            null,
            new Dictionary<string, string>());
        var turn = new ConversationTurn(
            "turn-001",
            session.Id,
            1,
            "check @linked.txt",
            null,
            DateTimeOffset.UtcNow,
            null,
            "agent",
            null,
            null,
            new Dictionary<string, string>());
        var request = new RunPromptRequest(
            "check @linked.txt",
            session.Id,
            workspace,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Text,
            null,
            PrimaryMode.Build,
            null);

        var act = async () => await resolver.ResolveAsync(
            workspace,
            workspace,
            session,
            turn,
            request,
            PrimaryMode.Build,
            isInteractive: false,
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*outside the workspace*");
    }
}
