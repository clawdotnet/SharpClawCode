namespace SharpClaw.Code.Memory.Models;

/// <summary>
/// Represents one indexed text chunk stored in the workspace knowledge store.
/// </summary>
/// <param name="Id">Stable chunk identifier.</param>
/// <param name="Path">Workspace-relative path.</param>
/// <param name="Language">Detected language tag.</param>
/// <param name="Excerpt">Concise excerpt.</param>
/// <param name="Content">Full chunk content.</param>
/// <param name="StartLine">1-based starting line.</param>
/// <param name="EndLine">1-based ending line.</param>
/// <param name="Embedding">Deterministic embedding vector.</param>
public sealed record IndexedWorkspaceChunk(
    string Id,
    string Path,
    string Language,
    string Excerpt,
    string Content,
    int StartLine,
    int EndLine,
    float[] Embedding);

/// <summary>
/// Represents one indexed symbol extracted from the workspace.
/// </summary>
/// <param name="Id">Stable symbol identifier.</param>
/// <param name="Path">Workspace-relative path.</param>
/// <param name="Name">Symbol name.</param>
/// <param name="Kind">Symbol kind.</param>
/// <param name="Container">Containing type or namespace.</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
public sealed record IndexedWorkspaceSymbol(
    string Id,
    string Path,
    string Name,
    string Kind,
    string? Container,
    int Line,
    int Column);

/// <summary>
/// Represents one project or package dependency edge discovered during indexing.
/// </summary>
/// <param name="SourcePath">Source project path.</param>
/// <param name="Target">Target project or package identifier.</param>
/// <param name="Kind">Edge kind.</param>
public sealed record IndexedWorkspaceProjectEdge(
    string SourcePath,
    string Target,
    string Kind);

/// <summary>
/// Represents the full workspace index document persisted in the knowledge store.
/// </summary>
/// <param name="Chunks">Indexed chunks.</param>
/// <param name="Symbols">Indexed symbols.</param>
/// <param name="ProjectEdges">Indexed dependency edges.</param>
public sealed record WorkspaceIndexDocument(
    IReadOnlyList<IndexedWorkspaceChunk> Chunks,
    IReadOnlyList<IndexedWorkspaceSymbol> Symbols,
    IReadOnlyList<IndexedWorkspaceProjectEdge> ProjectEdges);
