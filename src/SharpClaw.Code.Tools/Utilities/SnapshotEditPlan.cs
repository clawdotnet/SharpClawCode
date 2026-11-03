using System.Text;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Utilities;

/// <summary>Computes both the review and the publication from one exact, validated file image.</summary>
internal abstract record SnapshotEditPlan
{
    internal sealed record Rejected(string Status, string Message) : SnapshotEditPlan;

    internal sealed record Prepared(FileTextSnapshot Original, string UpdatedText, string ResultId, SnapshotLineEdit[] Edits) : SnapshotEditPlan
    {
        internal string PreviewId(string canonicalPath)
            => FileTextSnapshot.ComputeId(ToolJson.Serialize(new[] { "sharpclaw/snapshot-preview/v1", canonicalPath, Original.Id, ResultId }));

        internal SnapshotEditChange[] DescribeChanges() => Edits.Select(edit =>
        {
            var start = Original.Offset(edit.StartLine);
            var end = Original.Offset(edit.StartLine + edit.DeleteLineCount);
            return new SnapshotEditChange(edit.StartLine, edit.DeleteLineCount, Original.Text[start..end], edit.NewText);
        }).ToArray();
    }

    internal static SnapshotEditPlan Build(string? text, EditFileSnapshotArguments args)
    {
        if (text is null) return new Rejected("notFound", "File was not found.");
        var snapshot = new FileTextSnapshot(text);
        if (!string.Equals(snapshot.Id, args.SnapshotId, StringComparison.OrdinalIgnoreCase))
            return new Rejected("staleSnapshot", "File changed. Read a fresh snapshot before editing.");
        if (args.Edits is not { Length: > 0 and <= 100 } || args.Edits.Any(edit => edit is null || edit.NewText is null))
            return new Rejected("invalidEdits", "Supply between 1 and 100 valid edits.");

        var edits = args.Edits.OrderBy(edit => edit.StartLine).ToArray();
        for (var i = 0; i < edits.Length; i++)
        {
            var edit = edits[i];
            if (edit.StartLine < 1 || edit.DeleteLineCount < 0 || (long)edit.StartLine + edit.DeleteLineCount > snapshot.Starts.Length + 1L)
                return new Rejected("invalidEdits", "An edit is outside the original snapshot.");
            if (i > 0 && (edit.StartLine == edits[i - 1].StartLine || edit.StartLine < (long)edits[i - 1].StartLine + edits[i - 1].DeleteLineCount))
                return new Rejected("invalidEdits", "Edits overlap or share an insertion point.");
        }

        var builder = new StringBuilder(text.Length);
        var cursor = 0;
        foreach (var edit in edits)
        {
            var start = snapshot.Offset(edit.StartLine);
            builder.Append(text, cursor, start - cursor).Append(edit.NewText);
            cursor = snapshot.Offset(edit.StartLine + edit.DeleteLineCount);
        }
        builder.Append(text, cursor, text.Length - cursor);
        var updated = builder.ToString();
        return new Prepared(snapshot, updated, updated == text ? snapshot.Id : FileTextSnapshot.ComputeId(updated), edits);
    }

    private const string Properties = """
        {"type":"object","properties":{"path":{"type":"string"},"snapshotId":{"type":"string"},"edits":{"type":"array","minItems":1,"maxItems":100,"items":{"type":"object","properties":{"startLine":{"type":"integer","minimum":1},"deleteLineCount":{"type":"integer","minimum":0},"newText":{"type":"string"}},"required":["startLine","deleteLineCount","newText"],"additionalProperties":false}}
        """;
    private const string Required = """},"required":["path","snapshotId","edits"],"additionalProperties":false}""";
    internal const string PreviewSchema = Properties + Required;
    internal const string ApplySchema = Properties + ""","previewId":{"type":"string"}""" + Required;
}
