using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Loads compatible workspace and user instruction files for prompt assembly.
/// </summary>
public sealed class InstructionRuleService(
    IFileSystem fileSystem,
    IPathService pathService,
    IUserProfilePaths userProfilePaths) : IInstructionRuleService
{
    private const int MaxDocumentCount = 12;
    private const int MaxDocumentCharacters = 3_500;
    private const int MaxTotalCharacters = 12_000;

    /// <inheritdoc />
    public async Task<InstructionRuleSnapshot> LoadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var documents = new List<InstructionRuleDocument>();
        var budget = new RuleBudget();

        await AddDirectoryRulesAsync(
            pathService.Combine(userProfilePaths.GetUserSharpClawRoot(), "rules"),
            "global",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        await AddDirectoryRulesAsync(
            pathService.Combine(normalizedWorkspaceRoot, ".sharpclaw", "rules"),
            "workspace",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        await AddExplicitPathAsync(
            pathService.Combine(normalizedWorkspaceRoot, "AGENTS.md"),
            "workspace",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        await AddExplicitPathAsync(
            pathService.Combine(normalizedWorkspaceRoot, ".clinerules"),
            "workspace",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        await AddExplicitPathAsync(
            pathService.Combine(normalizedWorkspaceRoot, ".cursorrules"),
            "workspace",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        await AddExplicitPathAsync(
            pathService.Combine(normalizedWorkspaceRoot, ".windsurfrules"),
            "workspace",
            documents,
            cancellationToken,
            budget).ConfigureAwait(false);

        return new InstructionRuleSnapshot(documents);
    }

    private async Task AddExplicitPathAsync(
        string path,
        string sourceKind,
        List<InstructionRuleDocument> documents,
        CancellationToken cancellationToken,
        RuleBudget budget)
    {
        if (BudgetExhausted(documents, budget))
        {
            return;
        }

        if (fileSystem.FileExists(path))
        {
            await AddFileAsync(path, sourceKind, documents, cancellationToken, budget).ConfigureAwait(false);
            return;
        }

        if (fileSystem.DirectoryExists(path))
        {
            await AddDirectoryRulesAsync(path, sourceKind, documents, cancellationToken, budget).ConfigureAwait(false);
        }
    }

    private async Task AddDirectoryRulesAsync(
        string directory,
        string sourceKind,
        List<InstructionRuleDocument> documents,
        CancellationToken cancellationToken,
        RuleBudget budget)
    {
        if (!fileSystem.DirectoryExists(directory) || BudgetExhausted(documents, budget))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(pathService.GetFullPath(directory));

        while (pending.Count > 0 && !BudgetExhausted(documents, budget))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            foreach (var child in fileSystem.EnumerateDirectories(current).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).Reverse())
            {
                pending.Push(child);
            }

            foreach (var file in EnumerateRuleFiles(current))
            {
                if (BudgetExhausted(documents, budget))
                {
                    break;
                }

                await AddFileAsync(file, sourceKind, documents, cancellationToken, budget).ConfigureAwait(false);
            }
        }
    }

    private IEnumerable<string> EnumerateRuleFiles(string directory)
    {
        var markdownFiles = fileSystem.EnumerateFiles(directory, "*.md");
        var textFiles = fileSystem.EnumerateFiles(directory, "*.txt");
        return markdownFiles
            .Concat(textFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase);
    }

    private async Task AddFileAsync(
        string fullPath,
        string sourceKind,
        List<InstructionRuleDocument> documents,
        CancellationToken cancellationToken,
        RuleBudget budget)
    {
        if (BudgetExhausted(documents, budget))
        {
            return;
        }

        var content = await fileSystem.ReadAllTextIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var normalized = NormalizeContent(content);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        var remaining = Math.Max(0, MaxTotalCharacters - budget.TotalCharacters);
        if (remaining == 0)
        {
            return;
        }

        var maxForDocument = Math.Min(MaxDocumentCharacters, remaining);
        var isTruncated = normalized.Length > maxForDocument;
        var trimmed = isTruncated
            ? normalized[..Math.Max(0, maxForDocument - 28)].TrimEnd() + Environment.NewLine + "[Instruction truncated]"
            : normalized;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return;
        }

        documents.Add(new InstructionRuleDocument(
            SourceKind: sourceKind,
            DisplayPath: ToDisplayPath(fullPath),
            Content: trimmed,
            IsTruncated: isTruncated));
        budget.TotalCharacters += trimmed.Length;
    }

    private string ToDisplayPath(string fullPath)
    {
        var normalizedFullPath = pathService.GetFullPath(fullPath);
        var home = userProfilePaths.GetUserHomeDirectory();
        if (normalizedFullPath.StartsWith(home, StringComparison.Ordinal))
        {
            return "~" + normalizedFullPath[home.Length..];
        }

        return normalizedFullPath;
    }

    private static string NormalizeContent(string content)
    {
        var builder = new StringBuilder(content.Length);
        using var reader = new StringReader(content);
        string? line;
        var wroteAnyLine = false;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimEnd();
            if (!wroteAnyLine && string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (wroteAnyLine)
            {
                builder.AppendLine();
            }

            builder.Append(trimmed);
            wroteAnyLine = true;
        }

        return builder.ToString().Trim();
    }

    private static bool BudgetExhausted(List<InstructionRuleDocument> documents, RuleBudget budget)
        => documents.Count >= MaxDocumentCount || budget.TotalCharacters >= MaxTotalCharacters;

    private sealed class RuleBudget
    {
        public int TotalCharacters { get; set; }
    }
}
