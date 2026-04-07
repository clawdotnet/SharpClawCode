using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Edits a file by replacing one unique string occurrence.
/// </summary>
public sealed class EditFileTool(IFileSystem fileSystem, IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "edit_file";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Replace one unique string occurrence in a workspace file.",
        ApprovalScope: ApprovalScope.FileSystemWrite,
        IsDestructive: true,
        RequiresApproval: true,
        InputTypeName: nameof(EditFileToolArguments),
        InputDescription: "JSON object with path, oldString, and newString.",
        Tags: ["file", "edit", "workspace"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<EditFileToolArguments>(request);
        var pathResolver = new WorkspacePathResolver(pathService);
        var fullPath = pathResolver.ResolvePath(context, arguments.Path);
        var content = await fileSystem.ReadAllTextIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        if (content is null)
        {
            return CreateFailureResult(context, request, $"File '{arguments.Path}' was not found.");
        }

        var index = content.IndexOf(arguments.OldString, StringComparison.Ordinal);
        if (index < 0)
        {
            return CreateFailureResult(context, request, $"String '{arguments.OldString}' was not found in '{arguments.Path}'.");
        }

        var secondIndex = content.IndexOf(arguments.OldString, index + arguments.OldString.Length, StringComparison.Ordinal);
        if (secondIndex >= 0)
        {
            return CreateFailureResult(context, request, $"String '{arguments.OldString}' must be unique within '{arguments.Path}'.");
        }

        var updatedContent = string.Concat(
            content.AsSpan(0, index),
            arguments.NewString,
            content.AsSpan(index + arguments.OldString.Length));

        await fileSystem.WriteAllTextAsync(fullPath, updatedContent, cancellationToken).ConfigureAwait(false);

        context.MutationRecorder?.Record(
            new FileMutationOperation(
                OperationId: $"op-{Guid.NewGuid():N}",
                Kind: FileMutationKind.Replace,
                ToolName: ToolName,
                RelativePath: pathResolver.ToRelativePath(context, fullPath),
                ContentBefore: content,
                ContentAfter: updatedContent));

        var payload = new FileMutationToolResult(
            Path: pathResolver.ToRelativePath(context, fullPath),
            Message: $"Updated '{arguments.OldString}' in '{arguments.Path}'.");

        return CreateSuccessResult(context, request, payload.Message, payload);
    }
}
