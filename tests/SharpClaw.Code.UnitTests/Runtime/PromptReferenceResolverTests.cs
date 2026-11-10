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
    private const long MaxImageReferenceBytes = 8 * 1024 * 1024;

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
            new SessionApprovalMemory(),
            new AutoApprovalBudgetTracker());
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

    /// <summary>
    /// Ensures oversized image prompt references are described but not embedded into provider content.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_should_omit_structured_content_for_oversized_image_reference()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-ref-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var imagePath = Path.Combine(workspace, "large.png");
        await using (var image = new FileStream(imagePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            image.SetLength(MaxImageReferenceBytes + 1);
        }

        var resolver = CreateResolver();
        var session = CreateSession(workspace);
        var turn = CreateTurn(session, "check @large.png");
        var request = CreateRequest(session, workspace, "check @large.png");

        var resolution = await resolver.ResolveAsync(
            workspace,
            workspace,
            session,
            turn,
            request,
            PrimaryMode.Build,
            isInteractive: false,
            CancellationToken.None);

        resolution.ExpandedPrompt.Should().Contain("Referenced image omitted");
        resolution.StructuredContent.Should().BeEmpty();
        resolution.References.Should().ContainSingle()
            .Which.IncludedEntryCount.Should().Be(0);
    }

    /// <summary>
    /// Ensures directory prompt references stop at the configured file limit.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_should_limit_directory_reference_file_count()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-prompt-ref-tests", Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(workspace, "reference");
        Directory.CreateDirectory(directory);
        for (var i = 0; i < 25; i++)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, $"file-{i:D2}.txt"), $"content {i}");
        }

        var resolver = CreateResolver();
        var session = CreateSession(workspace);
        var turn = CreateTurn(session, "check @reference");
        var request = CreateRequest(session, workspace, "check @reference");

        var resolution = await resolver.ResolveAsync(
            workspace,
            workspace,
            session,
            turn,
            request,
            PrimaryMode.Build,
            isInteractive: false,
            CancellationToken.None);

        resolution.References.Should().ContainSingle()
            .Which.IncludedEntryCount.Should().Be(20);
        resolution.ExpandedPrompt.Should().Contain("file-00.txt");
        resolution.ExpandedPrompt.Should().Contain("file-19.txt");
        resolution.ExpandedPrompt.Should().NotContain("file-20.txt");
    }

    [Theory]
    [InlineData("email user@example.com")]
    [InlineData("Java @Override and @types/node")]
    public async Task Ordinary_at_mentions_remain_literal(string prompt)
    {
        var workspace = Path.GetTempPath();
        var session = CreateSession(workspace);
        var resolution = await CreateResolver().ResolveAsync(workspace, workspace, session,
            CreateTurn(session, prompt), CreateRequest(session, workspace, prompt), PrimaryMode.Build, false, CancellationToken.None);
        resolution.ExpandedPrompt.Should().Be(prompt);
        resolution.References.Should().BeEmpty();
    }

    private static PromptReferenceResolver CreateResolver()
    {
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
            new SessionApprovalMemory(),
            new AutoApprovalBudgetTracker());
        return new PromptReferenceResolver(new LocalFileSystem(), pathService, engine);
    }

    private static ConversationSession CreateSession(string workspace)
        => new(
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

    private static ConversationTurn CreateTurn(ConversationSession session, string prompt)
        => new(
            "turn-001",
            session.Id,
            1,
            prompt,
            null,
            DateTimeOffset.UtcNow,
            null,
            "agent",
            null,
            null,
            new Dictionary<string, string>());

    private static RunPromptRequest CreateRequest(ConversationSession session, string workspace, string prompt)
        => new(
            prompt,
            session.Id,
            workspace,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Text,
            null,
            PrimaryMode.Build,
            null);
}
