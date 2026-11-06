using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>Shows exact before/after fragments for a snapshot edit without acquiring a write lock or recording a mutation.</summary>
public sealed class PreviewFileSnapshotTool(IFileSystem files, IPathService paths) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "preview_file_snapshot";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName,
        "Preview a proposed edit against read_file_snapshot. Returns the exact changed fragments and a previewId for edit_file_snapshot.",
        ApprovalScope.ToolExecution, false, false, nameof(EditFileSnapshotArguments), "Path, snapshotId and 1-100 edits in original line coordinates.",
        ["file", "preview", "snapshot"], SnapshotEditPlan.PreviewSchema);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var args = DeserializeArguments<EditFileSnapshotArguments>(request);
        var resolver = new WorkspacePathResolver(paths);
        var path = resolver.ResolvePath(context, args.Path);
        var relative = resolver.ToRelativePath(context, path);
        var text = await files.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        var plan = SnapshotEditPlan.Build(text, args);
        if (plan is SnapshotEditPlan.Rejected problem)
        {
            var json = ToolJson.Serialize(new SnapshotEditPreview(relative, problem.Status, null, null, null, [], problem.Message));
            return CreateFailureResult(context, request, problem.Message) with
            {
                StructuredOutputJson = json,
                Output = context.OutputFormat == OutputFormat.Json ? json : null
            };
        }

        var edit = (SnapshotEditPlan.Prepared)plan;
        var unchanged = edit.Original.Text == edit.UpdatedText;
        var preview = new SnapshotEditPreview(relative, unchanged ? "unchanged" : "previewed", edit.Original.Id,
            edit.ResultId, edit.PreviewId(path), edit.DescribeChanges(), unchanged ? "No changes required." : "Proposed changes.");
        var description = string.Join('\n', preview.Changes.Select(change =>
            $"Line {change.StartLine} (replace {change.DeleteLineCount}): {ToolJson.Serialize(change.Before)} → {ToolJson.Serialize(change.After)}"));
        return CreateSuccessResult(context, request, $"{preview.Message} {relative}\nPreview: {preview.PreviewId}\nResult snapshot: {preview.ResultSnapshotId}\n{description}", preview);
    }
}
