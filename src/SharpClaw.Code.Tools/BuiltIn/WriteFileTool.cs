using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Writes a full file within the workspace boundary.
/// </summary>
public sealed class WriteFileTool(IFileSystem fileSystem, IPathService pathService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "write_file";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Write a file inside the workspace.",
        ApprovalScope: ApprovalScope.FileSystemWrite,
        IsDestructive: true,
        RequiresApproval: true,
        InputTypeName: nameof(WriteFileToolArguments),
        InputDescription: "JSON object with path and full replacement content.",
        Tags: ["file", "write", "workspace"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<WriteFileToolArguments>(request);
        var pathResolver = new WorkspacePathResolver(pathService);
        var fullPath = pathResolver.ResolvePath(context, arguments.Path);
        var relative = pathResolver.ToRelativePath(context, fullPath);
        var prior = await fileSystem.ReadAllTextIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var kind = prior is null ? FileMutationKind.Create : FileMutationKind.Replace;

        await fileSystem.WriteAllTextAsync(fullPath, arguments.Content, cancellationToken).ConfigureAwait(false);

        context.MutationRecorder?.Record(
            new FileMutationOperation(
                OperationId: $"op-{Guid.NewGuid():N}",
                Kind: kind,
                ToolName: ToolName,
                RelativePath: relative,
                ContentBefore: prior,
                ContentAfter: arguments.Content));

        var payload = new FileMutationToolResult(
            Path: relative,
            Message: $"Wrote {arguments.Content.Length} characters.");

        return CreateSuccessResult(context, request, payload.Message, payload);
    }
}
