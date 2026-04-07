using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Reads a file from the workspace while enforcing workspace boundaries.
/// </summary>
public sealed class ReadFileTool(IFileSystem fileSystem, IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "read_file";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Read a file from the workspace.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(ReadFileToolArguments),
        InputDescription: "JSON object with path and optional offset/limit line slicing.",
        Tags: ["file", "read", "workspace"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<ReadFileToolArguments>(request);
        var pathResolver = new WorkspacePathResolver(pathService);
        var fullPath = pathResolver.ResolvePath(context, arguments.Path);
        var lines = await fileSystem.ReadAllLinesIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (lines.Length == 0 && !fileSystem.FileExists(fullPath))
        {
            return CreateFailureResult(context, request, $"File '{arguments.Path}' was not found.");
        }

        var (startIndex, endIndex) = ComputeRange(lines.Length, arguments.Offset, arguments.Limit);
        var selectedLines = lines[startIndex..endIndex];
        var startLine = selectedLines.Length == 0 ? 0 : startIndex + 1;
        var endLine = selectedLines.Length == 0 ? 0 : endIndex;
        var formattedContent = string.Join(Environment.NewLine, selectedLines.Select((line, index) => $"{startLine + index}|{line}"));
        var payload = new ReadFileToolResult(
            Path: pathResolver.ToRelativePath(context, fullPath),
            Exists: true,
            Content: formattedContent,
            StartLine: startLine,
            EndLine: endLine,
            TotalLineCount: lines.Length);

        return CreateSuccessResult(context, request, formattedContent, payload);
    }

    private static (int StartIndex, int EndIndex) ComputeRange(int lineCount, int? offset, int? limit)
    {
        if (lineCount == 0)
        {
            return (0, 0);
        }

        var startIndex = offset switch
        {
            null => 0,
            > 0 => Math.Min(offset.Value - 1, lineCount),
            < 0 => Math.Max(lineCount + offset.Value, 0),
            _ => 0
        };

        var count = limit.GetValueOrDefault(lineCount - startIndex);
        count = Math.Max(count, 0);
        var endIndex = Math.Min(startIndex + count, lineCount);
        return (startIndex, endIndex);
    }
}
