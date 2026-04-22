using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Prompts;

/// <inheritdoc />
public sealed partial class PromptReferenceResolver(
    IFileSystem fileSystem,
    IPathService pathService,
    IPermissionPolicyEngine permissionPolicyEngine,
    IRuntimeHostContextAccessor? hostContextAccessor = null) : IPromptReferenceResolver
{
    /// <inheritdoc />
    public async Task<PromptReferenceResolution> ResolveAsync(
        string workspaceRoot,
        string workingDirectory,
        ConversationSession session,
        ConversationTurn turn,
        RunPromptRequest request,
        PrimaryMode primaryMode,
        bool isInteractive,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turn);

        var original = request.Prompt;
        var matches = AtPathRegex().Matches(original).Cast<Match>().OrderByDescending(m => m.Index).ToArray();
        if (matches.Length == 0)
        {
            return new PromptReferenceResolution(original, original, []);
        }

        var workspaceFull = pathService.GetCanonicalFullPath(workspaceRoot);
        var workDirFull = pathService.GetCanonicalFullPath(workingDirectory);
        var refs = new List<PromptReference>();
        var expanded = new StringBuilder(original);

        foreach (var match in matches.OrderByDescending(m => m.Index))
        {
            var rawToken = match.Value;
            var pathPart = match.Groups[1].Value.Split('#', 2)[0];
            if (string.IsNullOrWhiteSpace(pathPart))
            {
                throw new InvalidOperationException($"Empty path in prompt reference token '{rawToken}'.");
            }

            var resolvedFull = Path.IsPathRooted(pathPart)
                ? pathService.GetCanonicalFullPath(pathPart)
                : pathService.GetCanonicalFullPath(pathService.Combine(workDirFull, pathPart));

            var outside = !IsWithinWorkspace(workspaceFull, resolvedFull);
            if (outside)
            {
                await EnsureOutsideWorkspaceAllowedAsync(
                    session.Id,
                    turn.Id,
                    workDirFull,
                    workspaceFull,
                    request.PermissionMode,
                    primaryMode,
                    isInteractive,
                    request.ApprovalSettings,
                    request.Metadata is not null
                        && request.Metadata.TryGetValue("acp", out var acp)
                        && string.Equals(acp, "true", StringComparison.OrdinalIgnoreCase),
                    resolvedFull,
                    cancellationToken).ConfigureAwait(false);
            }

            var text = await fileSystem.ReadAllTextIfExistsAsync(resolvedFull, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                throw new InvalidOperationException($"Referenced path is missing or unreadable: '{resolvedFull}'.");
            }

            var display = ToDisplayPath(workspaceFull, workDirFull, resolvedFull);
            var block =
                $"[Referenced file: {display}]" + Environment.NewLine
                + text
                + Environment.NewLine
                + $"[End referenced file: {display}]";

            expanded.Remove(match.Index, match.Length);
            expanded.Insert(match.Index, block);

            refs.Add(new PromptReference(
                Kind: PromptReferenceKind.File,
                OriginalToken: rawToken,
                RequestedPath: pathPart,
                ResolvedFullPath: resolvedFull,
                DisplayPath: display,
                WasOutsideWorkspace: outside,
                IncludedContent: text));
        }

        refs.Reverse();
        return new PromptReferenceResolution(original, expanded.ToString(), refs);
    }

    private async Task EnsureOutsideWorkspaceAllowedAsync(
        string sessionId,
        string turnId,
        string workingDirectory,
        string workspaceRoot,
        PermissionMode permissionMode,
        PrimaryMode primaryMode,
        bool isInteractive,
        ApprovalSettings? approvalSettings,
        bool isAcp,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(
            new PromptOutsideWorkspaceReadArguments(absolutePath),
            ProtocolJsonContext.Default.PromptOutsideWorkspaceReadArguments);
        var toolRequest = new ToolExecutionRequest(
            Id: $"prompt-read-{Guid.NewGuid():N}",
            SessionId: sessionId,
            TurnId: turnId,
            ToolName: "prompt-outside-workspace-read",
            ArgumentsJson: json,
            ApprovalScope: ApprovalScope.PromptOutsideWorkspaceRead,
            WorkingDirectory: workingDirectory,
            RequiresApproval: true,
            IsDestructive: false);

        var context = new PermissionEvaluationContext(
            SessionId: sessionId,
            WorkspaceRoot: workspaceRoot,
            WorkingDirectory: workingDirectory,
            PermissionMode: permissionMode,
            AllowedTools: null,
            AllowDangerousBypass: false,
            IsInteractive: isInteractive,
            SourceKind: PermissionRequestSourceKind.Runtime,
            SourceName: isAcp ? "acp" : null,
            TrustedPluginNames: null,
            TrustedMcpServerNames: null,
            PrimaryMode: primaryMode,
            TenantId: hostContextAccessor?.Current?.TenantId,
            ApprovalSettings: approvalSettings);

        var decision = await permissionPolicyEngine
            .EvaluateAsync(toolRequest, context, cancellationToken)
            .ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(decision.Reason)
                    ? $"Read outside the workspace was denied for '{absolutePath}'."
                    : decision.Reason);
        }
    }

    private static bool IsWithinWorkspace(string workspaceRootFull, string candidateFull)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(workspaceRootFull, candidateFull, comparison))
        {
            return true;
        }

        var prefix = workspaceRootFull.EndsWith(Path.DirectorySeparatorChar.ToString(), comparison)
            ? workspaceRootFull
            : workspaceRootFull + Path.DirectorySeparatorChar;

        return candidateFull.StartsWith(prefix, comparison);
    }

    private static string ToDisplayPath(string workspaceRootFull, string workingDirectoryFull, string fullPath)
    {
        if (IsWithinWorkspace(workspaceRootFull, fullPath))
        {
            return Path.GetRelativePath(workspaceRootFull, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        }

        var relCwd = Path.GetRelativePath(workingDirectoryFull, fullPath);
        if (!relCwd.StartsWith("..", StringComparison.Ordinal))
        {
            return relCwd.Replace(Path.DirectorySeparatorChar, '/');
        }

        return fullPath;
    }

    [GeneratedRegex(@"@([^\s<>""|*?]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AtPathRegex();
}
