namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Identifies the durable scope for a memory entry.
/// </summary>
public enum MemoryScope
{
    /// <summary>
    /// Memory applies to the current workspace only.
    /// </summary>
    Project,

    /// <summary>
    /// Memory applies across workspaces for the current user.
    /// </summary>
    User,
}

/// <summary>
/// Identifies the category of a workspace search hit.
/// </summary>
public enum WorkspaceSearchHitKind
{
    /// <summary>
    /// The hit was produced from a lexical chunk match.
    /// </summary>
    Lexical,

    /// <summary>
    /// The hit was produced from symbol metadata.
    /// </summary>
    Symbol,

    /// <summary>
    /// The hit was produced from semantic embedding similarity.
    /// </summary>
    Semantic,
}

/// <summary>
/// Describes the configured local runtime flavor for an OpenAI-compatible endpoint.
/// </summary>
public enum LocalRuntimeKind
{
    /// <summary>
    /// A generic OpenAI-compatible runtime.
    /// </summary>
    Generic,

    /// <summary>
    /// Ollama.
    /// </summary>
    Ollama,

    /// <summary>
    /// llama.cpp server mode.
    /// </summary>
    LlamaCpp,
}

/// <summary>
/// Describes the authentication mode for a provider or runtime profile.
/// </summary>
public enum ProviderAuthMode
{
    /// <summary>
    /// API key is required.
    /// </summary>
    ApiKey,

    /// <summary>
    /// Authentication is optional.
    /// </summary>
    Optional,

    /// <summary>
    /// No authentication is expected.
    /// </summary>
    None,
}

/// <summary>
/// Represents one structured memory entry stored by SharpClaw.
/// </summary>
/// <param name="Id">Stable memory entry identifier.</param>
/// <param name="Scope">Project or user scope.</param>
/// <param name="Content">Memory content.</param>
/// <param name="Source">Origin of the memory item.</param>
/// <param name="SourceSessionId">Source session id when applicable.</param>
/// <param name="SourceTurnId">Source turn id when applicable.</param>
/// <param name="Tags">Optional tags.</param>
/// <param name="Confidence">Optional confidence score.</param>
/// <param name="RelatedFilePath">Optional related file path.</param>
/// <param name="RelatedSymbolName">Optional related symbol name.</param>
/// <param name="CreatedAtUtc">Creation timestamp.</param>
/// <param name="UpdatedAtUtc">Last update timestamp.</param>
public sealed record MemoryEntry(
    string Id,
    MemoryScope Scope,
    string Content,
    string Source,
    string? SourceSessionId,
    string? SourceTurnId,
    string[] Tags,
    double? Confidence,
    string? RelatedFilePath,
    string? RelatedSymbolName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Represents one ranked workspace search hit.
/// </summary>
/// <param name="Path">Workspace-relative or absolute path.</param>
/// <param name="Kind">Hit category.</param>
/// <param name="Score">Combined search score.</param>
/// <param name="Excerpt">Concise excerpt or summary.</param>
/// <param name="SymbolName">Related symbol name when applicable.</param>
/// <param name="SymbolKind">Related symbol kind when applicable.</param>
/// <param name="StartLine">Optional 1-based starting line.</param>
/// <param name="EndLine">Optional 1-based ending line.</param>
public sealed record WorkspaceSearchHit(
    string Path,
    WorkspaceSearchHitKind Kind,
    double Score,
    string Excerpt,
    string? SymbolName,
    string? SymbolKind,
    int? StartLine,
    int? EndLine);

/// <summary>
/// Describes a workspace search request.
/// </summary>
/// <param name="Query">The search text.</param>
/// <param name="Limit">Requested maximum hit count.</param>
/// <param name="IncludeSymbols">Whether symbol hits should be included.</param>
/// <param name="IncludeSemantic">Whether semantic ranking should be included.</param>
public sealed record WorkspaceSearchRequest(
    string Query,
    int? Limit,
    bool IncludeSymbols = true,
    bool IncludeSemantic = true);

/// <summary>
/// Represents a ranked workspace search response.
/// </summary>
/// <param name="Query">Executed query.</param>
/// <param name="GeneratedAtUtc">Search timestamp.</param>
/// <param name="IndexRefreshedAtUtc">Last successful index refresh, if any.</param>
/// <param name="Hits">Ranked hits.</param>
public sealed record WorkspaceSearchResult(
    string Query,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset? IndexRefreshedAtUtc,
    WorkspaceSearchHit[] Hits);

/// <summary>
/// Summarizes the current workspace knowledge index state.
/// </summary>
/// <param name="WorkspaceRoot">Indexed workspace root.</param>
/// <param name="RefreshedAtUtc">Last successful refresh timestamp.</param>
/// <param name="IndexedFileCount">Indexed file count.</param>
/// <param name="ChunkCount">Indexed text chunk count.</param>
/// <param name="SymbolCount">Indexed symbol count.</param>
/// <param name="ProjectEdgeCount">Indexed project/dependency edge count.</param>
public sealed record WorkspaceIndexStatus(
    string WorkspaceRoot,
    DateTimeOffset? RefreshedAtUtc,
    int IndexedFileCount,
    int ChunkCount,
    int SymbolCount,
    int ProjectEdgeCount);

/// <summary>
/// Represents the result of refreshing the workspace knowledge index.
/// </summary>
/// <param name="WorkspaceRoot">Indexed workspace root.</param>
/// <param name="RefreshedAtUtc">Refresh timestamp.</param>
/// <param name="IndexedFileCount">Indexed file count.</param>
/// <param name="ChunkCount">Indexed text chunk count.</param>
/// <param name="SymbolCount">Indexed symbol count.</param>
/// <param name="ProjectEdgeCount">Indexed project/dependency edge count.</param>
/// <param name="SkippedPaths">Paths skipped during indexing.</param>
public sealed record WorkspaceIndexRefreshResult(
    string WorkspaceRoot,
    DateTimeOffset RefreshedAtUtc,
    int IndexedFileCount,
    int ChunkCount,
    int SymbolCount,
    int ProjectEdgeCount,
    string[] SkippedPaths);

/// <summary>
/// Describes one discovered model surfaced by a provider or local runtime.
/// </summary>
/// <param name="Id">Stable model identifier.</param>
/// <param name="DisplayName">Human-readable name.</param>
/// <param name="SupportsTools">Whether chat tool calling is expected to work.</param>
/// <param name="SupportsEmbeddings">Whether the model can be used for embeddings.</param>
public sealed record ProviderDiscoveredModel(
    string Id,
    string DisplayName,
    bool SupportsTools,
    bool SupportsEmbeddings);

/// <summary>
/// Summarizes one configured local runtime profile.
/// </summary>
/// <param name="Name">Profile name.</param>
/// <param name="Kind">Runtime kind.</param>
/// <param name="BaseUrl">Runtime base URL.</param>
/// <param name="DefaultChatModel">Default chat model.</param>
/// <param name="DefaultEmbeddingModel">Default embedding model, if any.</param>
/// <param name="AuthMode">Configured auth mode.</param>
/// <param name="IsHealthy">Whether the last health probe succeeded.</param>
/// <param name="HealthDetail">Health probe detail.</param>
/// <param name="AvailableModels">Discovered models for the profile.</param>
public sealed record LocalRuntimeProfileSummary(
    string Name,
    LocalRuntimeKind Kind,
    string BaseUrl,
    string DefaultChatModel,
    string? DefaultEmbeddingModel,
    ProviderAuthMode AuthMode,
    bool IsHealthy,
    string? HealthDetail,
    ProviderDiscoveredModel[] AvailableModels);

/// <summary>
/// Represents an ACP memory save request.
/// </summary>
/// <param name="Scope">Target memory scope.</param>
/// <param name="Content">Memory content.</param>
/// <param name="Source">Memory source label.</param>
/// <param name="Tags">Optional tags.</param>
/// <param name="Confidence">Optional confidence score.</param>
/// <param name="RelatedFilePath">Optional related file path.</param>
/// <param name="RelatedSymbolName">Optional related symbol name.</param>
public sealed record MemorySaveRequest(
    MemoryScope Scope,
    string Content,
    string Source,
    string[]? Tags = null,
    double? Confidence = null,
    string? RelatedFilePath = null,
    string? RelatedSymbolName = null);

/// <summary>
/// Represents an ACP memory list request.
/// </summary>
/// <param name="Scope">Optional scope filter.</param>
/// <param name="Query">Optional free-text query.</param>
/// <param name="Limit">Maximum result count.</param>
public sealed record MemoryListRequest(
    MemoryScope? Scope,
    string? Query,
    int? Limit);
