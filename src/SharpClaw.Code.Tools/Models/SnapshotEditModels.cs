namespace SharpClaw.Code.Tools.Models;

/// <summary>Reads a bounded line window and a SHA-256 identifier for the complete decoded file text.</summary>
public sealed record ReadFileSnapshotArguments(string Path, int StartLine = 1, int Limit = 200);

/// <summary>A snapshot window. NextLine is null when the end of the file has been reached.</summary>
public sealed record FileSnapshotResult(string Path, string SnapshotId, string Content, int StartLine, int TotalLineCount, int? NextLine);

/// <summary>Replaces complete lines in the original snapshot. Zero deleted lines inserts before StartLine; NewText includes any required line terminators.</summary>
public sealed record SnapshotLineEdit(int StartLine, int DeleteLineCount, string NewText);

/// <summary>Applies non-overlapping edits against one exact file snapshot.</summary>
public sealed record EditFileSnapshotArguments(string Path, string SnapshotId, SnapshotLineEdit[] Edits);

/// <summary>Machine-readable snapshot edit outcome; failed operations leave the target untouched.</summary>
public sealed record SnapshotEditResult(string Path, string Status, string? SnapshotId, int AppliedEdits, string Message);
