using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>Validates an entire single-file edit batch before publishing or recording any mutation.</summary>
public sealed class EditFileSnapshotTool(IFileSystem fileSystem, IPathService paths, IConditionalFileWriter writer) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "edit_file_snapshot";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Apply non-overlapping line edits to an exact snapshot from read_file_snapshot. Optionally pass the previewId returned by preview_file_snapshot to require the reviewed result. NewText must include needed newline characters.",
        ApprovalScope.FileSystemWrite, true, true, nameof(EditFileSnapshotArguments), "Path, snapshotId and 1-100 edits. Each edit replaces deleteLineCount whole lines at startLine; zero inserts before that line.", ["file", "edit", "snapshot"],
        SnapshotEditPlan.ApplySchema);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var args = DeserializeArguments<EditFileSnapshotArguments>(request);
        var resolver = new WorkspacePathResolver(paths);
        var path = resolver.ResolvePath(context, args.Path);
        var relativePath = resolver.ToRelativePath(context, path);
        await using var lease = await fileSystem.AcquireExclusiveFileLockAsync(resolver.ResolvePath(context, ".sharpclaw/locks/semantic-write.lock"), cancellationToken).ConfigureAwait(false);
        var text = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        var plan = SnapshotEditPlan.Build(text, args);
        if (plan is SnapshotEditPlan.Rejected rejection) return Failure(rejection.Status, rejection.Message);
        var prepared = (SnapshotEditPlan.Prepared)plan;
        if (args.PreviewId is not null && !string.Equals(prepared.PreviewId(path), args.PreviewId, StringComparison.OrdinalIgnoreCase))
            return Failure("previewMismatch", "This batch does not match the reviewed preview. Preview it again.");
        if (prepared.UpdatedText == prepared.Original.Text) return Success(prepared.ResultId, 0, "No changes required.");
        if (!await writer.WriteAsync(path, prepared.Original.Text, prepared.UpdatedText, cancellationToken).ConfigureAwait(false))
            return Failure("staleSnapshot", "File changed before publication. Read a fresh snapshot.");
        // No await between publication and journal recording: cancellation cannot hide a successful edit.
        context.MutationRecorder?.Record(new FileMutationOperation($"op-{Guid.NewGuid():N}", FileMutationKind.Replace, ToolName, relativePath, prepared.Original.Text, prepared.UpdatedText));
        return Success(prepared.ResultId, prepared.Edits.Length, "Applied snapshot edits.");

        ToolResult Success(string id, int count, string message) => CreateSuccessResult(context, request, $"{message} Snapshot: {id}", new SnapshotEditResult(relativePath, "applied", id, count, message));
        ToolResult Failure(string status, string message)
        {
            var payload = new SnapshotEditResult(relativePath, status, null, 0, message);
            var json = ToolJson.Serialize(payload);
            return CreateFailureResult(context, request, message) with { StructuredOutputJson = json, Output = context.OutputFormat == OutputFormat.Json ? json : null };
        }
    }
}
