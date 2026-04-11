using System.Text.RegularExpressions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Searches workspace file contents using regular expressions.
/// </summary>
public sealed class GrepSearchTool(IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "grep_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search workspace file contents with a regular expression.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(GrepSearchToolArguments),
        InputDescription: "JSON object with a regex pattern plus optional glob and limit.",
        Tags: ["search", "grep", "regex", "file"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<GrepSearchToolArguments>(request);
        var regexOptions = arguments.CaseSensitive
            ? RegexOptions.CultureInvariant
            : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        Regex regex;
        try
        {
            regex = new Regex(arguments.Pattern, regexOptions);
        }
        catch (ArgumentException ex)
        {
            return CreateFailureResult(context, request, $"Invalid regex pattern: {ex.Message}");
        }

        var pathResolver = new WorkspacePathResolver(pathService);
        var workspaceRoot = pathResolver.ResolveWorkspaceRoot(context);
        var matches = new List<GrepSearchMatch>();
        foreach (var file in Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = pathResolver.ToRelativePath(context, file);
            if (!string.IsNullOrWhiteSpace(arguments.Glob) && !GlobPatternMatcher.IsMatch(arguments.Glob, relativePath))
            {
                continue;
            }

            var lines = await File.ReadAllLinesAsync(file, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!regex.IsMatch(lines[index]))
                {
                    continue;
                }

                matches.Add(new GrepSearchMatch(relativePath, index + 1, lines[index]));
                if (matches.Count >= arguments.Limit.GetValueOrDefault(50))
                {
                    goto Done;
                }
            }
        }

Done:
        var payload = new GrepSearchToolResult(arguments.Pattern, matches.ToArray());
        var textOutput = matches.Count == 0
            ? $"No matches for '{arguments.Pattern}'."
            : string.Join(Environment.NewLine, matches.Select(match => $"{match.Path}:{match.LineNumber}:{match.LineText}"));

        return CreateSuccessResult(context, request, textOutput, payload);
    }
}
