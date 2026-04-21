using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Configures a named local runtime profile that rides on the OpenAI-compatible provider.
/// </summary>
public sealed class LocalRuntimeProfileOptions
{
    /// <summary>
    /// Gets or sets the runtime kind.
    /// </summary>
    public LocalRuntimeKind Kind { get; set; } = LocalRuntimeKind.Generic;

    /// <summary>
    /// Gets or sets the runtime base URL.
    /// </summary>
    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/v1/";

    /// <summary>
    /// Gets or sets the default chat model id.
    /// </summary>
    public string DefaultChatModel { get; set; } = "default";

    /// <summary>
    /// Gets or sets the default embedding model id.
    /// </summary>
    public string? DefaultEmbeddingModel { get; set; }

    /// <summary>
    /// Gets or sets the runtime auth mode.
    /// </summary>
    public ProviderAuthMode AuthMode { get; set; } = ProviderAuthMode.Optional;

    /// <summary>
    /// Gets or sets whether chat tool calling is expected to work.
    /// </summary>
    public bool SupportsToolCalls { get; set; } = true;

    /// <summary>
    /// Gets or sets whether embeddings are expected to work.
    /// </summary>
    public bool SupportsEmbeddings { get; set; }

    /// <summary>
    /// Gets or sets the optional API key for the local runtime.
    /// </summary>
    public string? ApiKey { get; set; }
}
