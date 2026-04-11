using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Searches workspace files using glob patterns.
/// </summary>
public sealed class GlobSearchTool(IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "glob_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search workspace files by glob pattern.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(GlobSearchToolArguments),
        InputDescription: "JSON object with a glob pattern and optional limit.",
        Tags: ["search", "glob", "file"]);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<GlobSearchToolArguments>(request);
        var pathResolver = new WorkspacePathResolver(pathService);
        var workspaceRoot = pathResolver.ResolveWorkspaceRoot(context);
        cancellationToken.ThrowIfCancellationRequested();
        var matches = Directory.EnumerateFiles(workspaceRoot, "*", SearchOption.AllDirectories)
            .TakeWhile(_ => !cancellationToken.IsCancellationRequested)
            .Select(path => pathResolver.ToRelativePath(context, path))
            .Where(path => GlobPatternMatcher.IsMatch(arguments.Pattern, path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(arguments.Limit.GetValueOrDefault(50))
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();

        var payload = new GlobSearchToolResult(arguments.Pattern, matches);
        var textOutput = matches.Length == 0
            ? $"No files matched '{arguments.Pattern}'."
            : string.Join(Environment.NewLine, matches);

        return Task.FromResult(CreateSuccessResult(context, request, textOutput, payload));
    }
}
