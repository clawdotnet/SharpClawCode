namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Maps a model alias to a provider name and concrete provider model id.
/// </summary>
/// <param name="ProviderName">The resolved provider name.</param>
/// <param name="ModelId">The resolved concrete model id.</param>
public sealed record ModelAliasDefinition(string ProviderName, string ModelId);
