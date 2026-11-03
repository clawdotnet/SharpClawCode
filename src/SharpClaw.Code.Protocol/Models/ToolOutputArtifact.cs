namespace SharpClaw.Code.Protocol.Models;

/// <summary>Complete provider-facing tool text retained outside the active model context.</summary>
public sealed record ToolOutputArtifact(string Id, string SessionId, string TurnId, string ToolName, DateTimeOffset CreatedAtUtc, string Content);

/// <summary>A bounded UTF-16 character window into a session-owned output artifact.</summary>
public sealed record ToolOutputArtifactPage(string ArtifactId, int Offset, string Content, int TotalCharacters, int? NextOffset);
