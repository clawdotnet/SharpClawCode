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
    private const int MaxDirectoryReferenceFiles = 20;
    private const int MaxDirectoryReferenceBytes = 200 * 1024;
    private const long MaxImageReferenceBytes = 8 * 1024 * 1024;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };
    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".pdf", ".zip", ".gz", ".tar", ".dll", ".exe", ".so", ".dylib", ".bin"
    };

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
            return new PromptReferenceResolution(original, original, [], []);
        }

        var workspaceFull = pathService.GetCanonicalFullPath(workspaceRoot);
        var workDirFull = pathService.GetCanonicalFullPath(workingDirectory);
        var refs = new List<PromptReference>();
        var expanded = new StringBuilder(original);
        var structuredContent = new List<ContentBlock>();

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

            if (!fileSystem.FileExists(resolvedFull) && !fileSystem.DirectoryExists(resolvedFull)
                && !pathPart.StartsWith('.') && !Path.IsPathRooted(pathPart))
            {
                continue; // Email, annotations, and package mentions are ordinary prompt text.
            }

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

            var display = ToDisplayPath(workspaceFull, workDirFull, resolvedFull);
            var (block, promptReference, extraContent) = await ResolveReferenceAsync(
                    resolvedFull,
                    display,
                    rawToken,
                    pathPart,
                    outside,
                    cancellationToken)
                .ConfigureAwait(false);

            expanded.Remove(match.Index, match.Length);
            expanded.Insert(match.Index, block);
            refs.Add(promptReference);
            if (extraContent is not null)
            {
                structuredContent.Add(extraContent);
            }
        }

        refs.Reverse();
        structuredContent.Reverse();
        return new PromptReferenceResolution(original, expanded.ToString(), refs, structuredContent);
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

    private async Task<(string ExpandedText, PromptReference Reference, ContentBlock? StructuredContent)> ResolveReferenceAsync(
        string resolvedFull,
        string display,
        string rawToken,
        string pathPart,
        bool outsideWorkspace,
        CancellationToken cancellationToken)
    {
        if (Directory.Exists(resolvedFull))
        {
            var (rendered, count) = await RenderDirectoryReferenceAsync(resolvedFull, display, cancellationToken).ConfigureAwait(false);
            return (
                rendered,
                new PromptReference(
                    PromptReferenceKind.Directory,
                    rawToken,
                    pathPart,
                    resolvedFull,
                    display,
                    outsideWorkspace,
                    rendered,
                    IncludedEntryCount: count),
                null);
        }

        if (ImageExtensions.Contains(Path.GetExtension(resolvedFull)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(resolvedFull);
            if (!fileInfo.Exists)
            {
                throw new InvalidOperationException($"Referenced path is missing or unreadable: '{resolvedFull}'.");
            }

            var mediaType = ResolveMediaType(resolvedFull);
            if (fileInfo.Length > MaxImageReferenceBytes)
            {
                var omittedPlaceholder =
                    $"[Referenced image omitted: {display} exceeds {FormatBytes(MaxImageReferenceBytes)}]" + Environment.NewLine
                    + $"[End referenced image: {display}]";
                return (
                    omittedPlaceholder,
                    new PromptReference(
                        PromptReferenceKind.Image,
                        rawToken,
                        pathPart,
                        resolvedFull,
                        display,
                        outsideWorkspace,
                        omittedPlaceholder,
                        MediaType: mediaType,
                        IncludedEntryCount: 0),
                    null);
            }

            var bytes = await File.ReadAllBytesAsync(resolvedFull, cancellationToken).ConfigureAwait(false);
            var placeholder =
                $"[Referenced image: {display} ({mediaType})]" + Environment.NewLine
                + $"[End referenced image: {display}]";
            return (
                placeholder,
                new PromptReference(
                    PromptReferenceKind.Image,
                    rawToken,
                    pathPart,
                    resolvedFull,
                    display,
                    outsideWorkspace,
                    placeholder,
                    MediaType: mediaType,
                    IncludedEntryCount: 1),
                new ContentBlock(
                    ContentBlockKind.Image,
                    Text: display,
                    ToolUseId: null,
                    ToolName: null,
                    ToolInputJson: null,
                    IsError: null,
                    MediaType: mediaType,
                    Data: Convert.ToBase64String(bytes),
                    Uri: resolvedFull));
        }

        if (File.Exists(resolvedFull) && new FileInfo(resolvedFull).Length > MaxDirectoryReferenceBytes)
            throw new InvalidOperationException($"Referenced file exceeds {FormatBytes(MaxDirectoryReferenceBytes)}: '{display}'.");
        var text = await fileSystem.ReadAllTextIfExistsAsync(resolvedFull, cancellationToken).ConfigureAwait(false);
        if (text is null)
        {
            throw new InvalidOperationException($"Referenced path is missing or unreadable: '{resolvedFull}'.");
        }

        return (
            $"[Referenced file: {display}]" + Environment.NewLine
            + text
            + Environment.NewLine
            + $"[End referenced file: {display}]",
            new PromptReference(
                PromptReferenceKind.File,
                rawToken,
                pathPart,
                resolvedFull,
                display,
                outsideWorkspace,
                text,
                IncludedEntryCount: 1),
            null);
    }

    private static async Task<(string Rendered, int FileCount)> RenderDirectoryReferenceAsync(
        string directoryPath,
        string display,
        CancellationToken cancellationToken)
    {
        var included = new List<(string RelativePath, string Content)>();
        var totalBytes = 0;
        foreach (var file in EnumerateReferenceFiles(directoryPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (included.Count >= MaxDirectoryReferenceFiles)
            {
                break;
            }

            if (BinaryExtensions.Contains(Path.GetExtension(file)))
            {
                continue;
            }

            long fileLength;
            try
            {
                fileLength = new FileInfo(file).Length;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var remainingBytes = MaxDirectoryReferenceBytes - totalBytes;
            if (fileLength > remainingBytes)
            {
                break;
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
            {
                continue;
            }

            if (text.IndexOf('\0') >= 0)
            {
                continue;
            }

            var bytes = Encoding.UTF8.GetByteCount(text);
            if (totalBytes + bytes > MaxDirectoryReferenceBytes)
            {
                break;
            }

            totalBytes += bytes;
            included.Add((Path.GetRelativePath(directoryPath, file).Replace(Path.DirectorySeparatorChar, '/'), text));
        }

        var builder = new StringBuilder();
        builder.Append("[Referenced directory: ").Append(display).AppendLine("]");
        builder.AppendLine("Manifest:");
        foreach (var entry in included)
        {
            builder.Append("- ").AppendLine(entry.RelativePath);
        }

        foreach (var entry in included)
        {
            builder.AppendLine()
                .Append("[Referenced file: ")
                .Append(entry.RelativePath)
                .AppendLine("]")
                .AppendLine(entry.Content)
                .Append("[End referenced file: ")
                .Append(entry.RelativePath)
                .AppendLine("]");
        }

        builder.Append("[End referenced directory: ").Append(display).Append(']');
        return (builder.ToString(), included.Count);
    }

    private static IEnumerable<string> EnumerateReferenceFiles(string directoryPath, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(directoryPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var file in EnumerateSortedFiles(current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldSkipPath(file))
                {
                    yield return file;
                }
            }

            var directories = EnumerateSortedDirectories(current);
            for (var i = directories.Length - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!ShouldSkipPath(directories[i]))
                {
                    pending.Push(directories[i]);
                }
            }
        }
    }

    private static string[] EnumerateSortedFiles(string directoryPath)
    {
        try
        {
            return Directory.EnumerateFiles(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] EnumerateSortedDirectories(string directoryPath)
    {
        try
        {
            return Directory.EnumerateDirectories(directoryPath, "*", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool ShouldSkipPath(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(static segment => segment is ".git" or ".sharpclaw" or "bin" or "obj");

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024 * 1024)} MiB"
            : $"{bytes / 1024} KiB";

    private static string ResolveMediaType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

    [GeneratedRegex(@"(?<![\w@])@([^\s<>""|*?]+)", RegexOptions.CultureInvariant)]
    private static partial Regex AtPathRegex();
}
