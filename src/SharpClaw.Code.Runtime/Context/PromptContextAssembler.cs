using System.Text.Json;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Skills.Abstractions;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Builds enriched prompt context from project memory, session state, skills, and git inspection.
/// </summary>
public sealed class PromptContextAssembler(
    IProjectMemoryService projectMemoryService,
    ISessionSummaryService sessionSummaryService,
    ISkillRegistry skillRegistry,
    IGitWorkspaceService gitWorkspaceService,
    IPromptReferenceResolver promptReferenceResolver) : IPromptContextAssembler
{
    /// <inheritdoc />
    public async Task<PromptExecutionContext> AssembleAsync(
        ConversationSession session,
        ConversationTurn turn,
        RunPromptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentNullException.ThrowIfNull(request);

        var workspaceRoot = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? session.WorkingDirectory ?? "."
            : request.WorkingDirectory;
        var memoryContextTask = projectMemoryService.BuildContextAsync(workspaceRoot, cancellationToken);
        var sessionSummaryTask = sessionSummaryService.BuildSummaryAsync(session, cancellationToken);
        var skillsTask = skillRegistry.ListAsync(workspaceRoot, cancellationToken);
        var gitTask = gitWorkspaceService.GetSnapshotAsync(workspaceRoot, cancellationToken);

        await Task.WhenAll(memoryContextTask, sessionSummaryTask, skillsTask, gitTask).ConfigureAwait(false);

        var memoryContext = await memoryContextTask.ConfigureAwait(false);
        var sessionSummary = await sessionSummaryTask.ConfigureAwait(false);
        var skills = await skillsTask.ConfigureAwait(false);
        var gitSnapshot = await gitTask.ConfigureAwait(false);

        var metadata = request.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal);

        if (!metadata.ContainsKey("model")
            && memoryContext.RepositorySettings.TryGetValue("defaultModel", out var defaultModel)
            && !string.IsNullOrWhiteSpace(defaultModel))
        {
            metadata["model"] = defaultModel;
        }

        metadata["workspaceRoot"] = workspaceRoot;
        metadata["skillCount"] = skills.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (memoryContext.Memory is not null)
        {
            metadata["projectMemoryLoaded"] = "true";
        }

        if (gitSnapshot.IsRepository)
        {
            metadata["gitBranch"] = gitSnapshot.CurrentBranch ?? string.Empty;
        }

        var workDir = string.IsNullOrWhiteSpace(request.WorkingDirectory)
            ? session.WorkingDirectory ?? workspaceRoot
            : request.WorkingDirectory;
        var effectivePrimary = PrimaryModeResolver.ResolveEffective(request, session);
        var refResolution = await promptReferenceResolver
            .ResolveAsync(
                workspaceRoot,
                workDir,
                session,
                turn,
                request,
                effectivePrimary,
                isInteractive: true,
                cancellationToken)
            .ConfigureAwait(false);

        metadata[SharpClawWorkflowMetadataKeys.OriginalPrompt] = refResolution.OriginalPrompt;
        metadata[SharpClawWorkflowMetadataKeys.PromptReferencesJson] = JsonSerializer.Serialize(
            refResolution.References,
            ProtocolJsonContext.Default.ListPromptReference);

        var sections = new List<string>();
        var memorySection = memoryContext.RenderForPrompt();
        if (!string.IsNullOrWhiteSpace(memorySection))
        {
            sections.Add(memorySection);
        }

        if (!string.IsNullOrWhiteSpace(sessionSummary))
        {
            sections.Add($"Session summary:\n{sessionSummary}");
        }

        if (skills.Count > 0)
        {
            sections.Add(
                "Available skills:\n"
                + string.Join(Environment.NewLine, skills.Select(skill => $"- {skill.Id}: {skill.Description}")));
        }

        sections.Add(gitSnapshot.RenderForPrompt());
        sections.Add($"User request:\n{refResolution.ExpandedPrompt}");

        return new PromptExecutionContext(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, sections),
            Metadata: metadata);
    }
}
