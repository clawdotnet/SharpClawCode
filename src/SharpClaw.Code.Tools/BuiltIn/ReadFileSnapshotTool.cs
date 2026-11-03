using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>Reads a bounded window and snapshot identifier from the same file read.</summary>
public sealed class ReadFileSnapshotTool(IFileSystem fileSystem, IPathService paths) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "read_file_snapshot";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Read a file window with a snapshot ID for edit_file_snapshot.", ApprovalScope.ToolExecution, false, false,
        nameof(ReadFileSnapshotArguments), "Path and optional one-based startLine and limit (default 200, maximum 500).", ["file", "read", "snapshot"],
        """{"type":"object","properties":{"path":{"type":"string"},"startLine":{"type":"integer","minimum":1},"limit":{"type":"integer","minimum":1,"maximum":500}},"required":["path"],"additionalProperties":false}""");

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var args = DeserializeArguments<ReadFileSnapshotArguments>(request);
        if (args.StartLine < 1 || args.Limit is < 1 or > 500) return CreateFailureResult(context, request, "startLine must be positive and limit must be between 1 and 500.");
        var resolver = new WorkspacePathResolver(paths);
        var path = resolver.ResolvePath(context, args.Path);
        var text = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (text is null) return CreateFailureResult(context, request, $"File '{args.Path}' was not found.");
        var snapshot = new FileTextSnapshot(text);
        var start = Math.Min(args.StartLine, snapshot.Starts.Length + 1);
        var end = (int)Math.Min((long)start + args.Limit, snapshot.Starts.Length + 1L);
        var lines = Enumerable.Range(start, end - start).Select(line => $"{line}|{text[snapshot.Offset(line)..snapshot.Offset(line + 1)].TrimEnd('\r', '\n')}");
        var payload = new FileSnapshotResult(resolver.ToRelativePath(context, path), snapshot.Id, string.Join('\n', lines), start, snapshot.Starts.Length, end <= snapshot.Starts.Length ? end : null);
        return CreateSuccessResult(context, request, $"Snapshot: {payload.SnapshotId}\n{payload.Content}\nNext line: {payload.NextLine?.ToString() ?? "end"}", payload);
    }
}
