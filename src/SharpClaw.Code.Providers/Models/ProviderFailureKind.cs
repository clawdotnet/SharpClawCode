namespace SharpClaw.Code.Providers.Models;

/// <summary>
/// Classifies the stage at which a provider-backed turn failed.
/// </summary>
public enum ProviderFailureKind
{
    /// <summary>
    /// The requested provider could not be resolved from the registered catalog.
    /// </summary>
    MissingProvider,

    /// <summary>
    /// Authentication status could not be determined because the auth probe failed.
    /// </summary>
    AuthenticationUnavailable,

    /// <summary>
    /// The provider failed while starting or streaming the request.
    /// </summary>
    StreamFailed,
}
