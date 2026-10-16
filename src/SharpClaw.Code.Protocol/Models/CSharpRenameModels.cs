namespace SharpClaw.Code.Protocol.Models;

/// <summary>A physical text edit with optimistic concurrency preconditions.</summary>
public sealed record CSharpRenameChange(string Path, string ContentBefore, string ContentAfter);
/// <summary>The fully calculated rename, before any files are written.</summary>
public sealed record CSharpRenamePlan(CSharpSymbolResolutionResult Resolution, string NewName, CSharpRenameChange[] Changes, string[] Errors);
/// <summary>The committed or rolled-back rename outcome. Residual paths remain recoverable mutations.</summary>
public sealed record CSharpRenameResult(bool Succeeded, CSharpSymbolResolutionResult Resolution, string NewName, string[] ChangedPaths, string[] Errors, string[] ResidualPaths);
