namespace SharpClaw.Code.Protocol.Models;

/// <summary>Version-tolerant working state carried across automatic compaction boundaries.</summary>
public sealed record CompactionWorkingState(
    string Objective,
    string[] Constraints,
    string[] Decisions,
    string[] ChangedFiles,
    string[] Verification,
    string[] OutstandingWork);
