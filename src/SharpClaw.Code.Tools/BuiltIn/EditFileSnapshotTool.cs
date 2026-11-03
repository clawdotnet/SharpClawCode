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
    public override ToolDefinition Definition { get; } = new(ToolName, "Apply non-overlapping line edits to an exact snapshot from read_file_snapshot. All line numbers refer to that original snapshot. NewText must include needed newline characters.",
        ApprovalScope.FileSystemWrite, true, true, nameof(EditFileSnapshotArguments), "Path, snapshotId and 1-100 edits. Each edit replaces deleteLineCount whole lines at startLine; zero inserts before that line.", ["file", "edit", "snapshot"],
        """{"type":"object","properties":{"path":{"type":"string"},"snapshotId":{"type":"string"},"edits":{"type":"array","minItems":1,"maxItems":100,"items":{"type":"object","properties":{"startLine":{"type":"integer","minimum":1},"deleteLineCount":{"type":"integer","minimum":0},"newText":{"type":"string"}},"required":["startLine","deleteLineCount","newText"],"additionalProperties":false}}},"required":["path","snapshotId","edits"],"additionalProperties":false}""");

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var args = DeserializeArguments<EditFileSnapshotArguments>(request);
        var resolver = new WorkspacePathResolver(paths);
        var path = resolver.ResolvePath(context, args.Path);
        var relativePath = resolver.ToRelativePath(context, path);
        await using var lease = await fileSystem.AcquireExclusiveFileLockAsync(resolver.ResolvePath(context, ".sharpclaw/locks/semantic-write.lock"), cancellationToken).ConfigureAwait(false);
        var text = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (text is null) return Failure("notFound", "File was not found.");
        var snapshot = new FileTextSnapshot(text);
        if (!string.Equals(snapshot.Id, args.SnapshotId, StringComparison.OrdinalIgnoreCase)) return Failure("staleSnapshot", "File changed. Read a fresh snapshot before editing.");
        if (args.Edits is not { Length: > 0 and <= 100 } || args.Edits.Any(edit => edit is null || edit.NewText is null)) return Failure("invalidEdits", "Supply between 1 and 100 valid edits.");
        var edits = args.Edits.OrderBy(edit => edit.StartLine).ToArray();
        for (var i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            if (edit.StartLine < 1 || edit.DeleteLineCount < 0 || (long)edit.StartLine + edit.DeleteLineCount > snapshot.Starts.Length + 1L)
                return Failure("invalidEdits", "An edit is outside the original snapshot.");
            if (i > 0 && (edit.StartLine == edits[i - 1].StartLine || edit.StartLine < (long)edits[i - 1].StartLine + edits[i - 1].DeleteLineCount))
                return Failure("invalidEdits", "Edits overlap or share an insertion point.");
        }
        var updated = text;
        foreach (var edit in edits.Reverse())
            updated = string.Concat(updated.AsSpan(0, snapshot.Offset(edit.StartLine)), edit.NewText, updated.AsSpan(snapshot.Offset(edit.StartLine + edit.DeleteLineCount)));
        if (updated == text) return Success(snapshot.Id, 0, "No changes required.");
        if (!await writer.WriteAsync(path, text, updated, cancellationToken).ConfigureAwait(false)) return Failure("staleSnapshot", "File changed before publication. Read a fresh snapshot.");
        // No await between publication and journal recording: cancellation cannot hide a successful edit.
        context.MutationRecorder?.Record(new FileMutationOperation($"op-{Guid.NewGuid():N}", FileMutationKind.Replace, ToolName, relativePath, text, updated));
        return Success(new FileTextSnapshot(updated).Id, edits.Length, "Applied snapshot edits.");

        ToolResult Success(string id, int count, string message) => CreateSuccessResult(context, request, $"{message} Snapshot: {id}", new SnapshotEditResult(relativePath, "applied", id, count, message));
        ToolResult Failure(string status, string message)
        {
            var payload = new SnapshotEditResult(relativePath, status, null, 0, message);
            var json = ToolJson.Serialize(payload);
            return CreateFailureResult(context, request, message) with { StructuredOutputJson = json, Output = context.OutputFormat == OutputFormat.Json ? json : null };
        }
    }
}
