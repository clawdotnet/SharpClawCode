using System.Text.Json;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;
using SharpClaw.Code.Sessions.Abstractions;
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
    IPromptReferenceResolver promptReferenceResolver,
    ISpecWorkflowService specWorkflowService,
    IWorkspaceDiagnosticsService workspaceDiagnosticsService,
    ITodoService todoService,
    IEventStore eventStore) : IPromptContextAssembler
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
        var diagnosticsTask = workspaceDiagnosticsService.BuildSnapshotAsync(workspaceRoot, cancellationToken);
        var todoTask = todoService.GetSnapshotAsync(workspaceRoot, session.Id, cancellationToken);

        await Task.WhenAll(memoryContextTask, sessionSummaryTask, skillsTask, gitTask, diagnosticsTask, todoTask).ConfigureAwait(false);

        var memoryContext = await memoryContextTask.ConfigureAwait(false);
        var sessionSummary = await sessionSummaryTask.ConfigureAwait(false);
        var skills = await skillsTask.ConfigureAwait(false);
        var gitSnapshot = await gitTask.ConfigureAwait(false);
        var diagnostics = await diagnosticsTask.ConfigureAwait(false);
        var todoSnapshot = await todoTask.ConfigureAwait(false);

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
                isInteractive: request.IsInteractive,
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

        var activeSessionTodos = todoSnapshot.SessionTodos.Where(static item => item.Status != TodoStatus.Done).Take(5).ToArray();
        var activeWorkspaceTodos = todoSnapshot.WorkspaceTodos.Where(static item => item.Status != TodoStatus.Done).Take(5).ToArray();
        if (activeSessionTodos.Length > 0 || activeWorkspaceTodos.Length > 0)
        {
            var lines = new List<string>();
            if (activeSessionTodos.Length > 0)
            {
                lines.Add("Session tasks:");
                lines.AddRange(activeSessionTodos.Select(static item => $"- [{item.Status}] {item.Title}"));
            }

            if (activeWorkspaceTodos.Length > 0)
            {
                lines.Add("Workspace tasks:");
                lines.AddRange(activeWorkspaceTodos.Select(static item => $"- [{item.Status}] {item.Title}"));
            }

            sections.Add(string.Join(Environment.NewLine, lines));
        }

        sections.Add(gitSnapshot.RenderForPrompt());
        if (diagnostics.Diagnostics.Count > 0 || diagnostics.ConfiguredLspServers.Count > 0)
        {
            var topDiagnostics = diagnostics.Diagnostics.Take(5)
                .Select(item => $"- {item.Severity}: {item.Message}" + (string.IsNullOrWhiteSpace(item.FilePath) ? string.Empty : $" ({item.FilePath})"));
            sections.Add(
                "Workspace diagnostics:\n"
                + $"Configured sources: {diagnostics.ConfiguredLspServers.Count}\n"
                + $"Errors: {diagnostics.Diagnostics.Count(item => item.Severity == WorkspaceDiagnosticSeverity.Error)}, "
                + $"Warnings: {diagnostics.Diagnostics.Count(item => item.Severity == WorkspaceDiagnosticSeverity.Warning)}\n"
                + string.Join(Environment.NewLine, topDiagnostics));
        }

        if (effectivePrimary == Protocol.Enums.PrimaryMode.Spec)
        {
            sections.Add(specWorkflowService.BuildPromptInstructions());
        }

        sections.Add($"User request:\n{refResolution.ExpandedPrompt}");

        // Assemble prior-turn conversation history for multi-turn context.
        // NOTE: This reads the full event log per turn, which scales linearly with session length.
        // For long-running sessions, consider persisting a compacted message history in session metadata
        // to avoid re-reading and re-assembling the full log on every prompt.
        const int MaxHistoryTokenBudget = 100_000;
        IReadOnlyList<ChatMessage> conversationHistory = [];
        if (turn.SequenceNumber > 1)
        {
            var sessionEvents = await eventStore
                .ReadAllAsync(workspaceRoot, session.Id, cancellationToken)
                .ConfigureAwait(false);
            var rawHistory = ConversationHistoryAssembler.Assemble(sessionEvents);
            conversationHistory = ContextWindowManager.Truncate(rawHistory, MaxHistoryTokenBudget);
        }

        return new PromptExecutionContext(
            Prompt: string.Join(Environment.NewLine + Environment.NewLine, sections),
            Metadata: metadata,
            ConversationHistory: conversationHistory);
    }
}
