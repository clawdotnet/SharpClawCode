using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Models;

/// <summary>
/// Represents a classified provider failure that should fail the active turn rather than degrade to placeholder output.
/// </summary>
public sealed class ProviderExecutionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderExecutionException"/> class.
    /// </summary>
    /// <param name="providerName">The provider name involved in the failure.</param>
    /// <param name="model">The requested model id.</param>
    /// <param name="kind">The classified failure kind.</param>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    /// <param name="providerRequest">The provider request active when the failure occurred, if available.</param>
    /// <param name="providerEvents">The provider events observed before the failure, if available.</param>
    public ProviderExecutionException(
        string providerName,
        string model,
        ProviderFailureKind kind,
        string message,
        Exception? innerException = null,
        ProviderRequest? providerRequest = null,
        IReadOnlyList<ProviderEvent>? providerEvents = null)
        : base(message, innerException)
    {
        ProviderName = providerName;
        Model = model;
        Kind = kind;
        ProviderRequest = providerRequest;
        ProviderEvents = providerEvents;
    }

    /// <summary>
    /// Gets the provider name involved in the failure.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the requested model id.
    /// </summary>
    public string Model { get; }

    /// <summary>
    /// Gets the classified provider failure kind.
    /// </summary>
    public ProviderFailureKind Kind { get; }

    /// <summary>
    /// Gets the provider request active when the failure occurred, if available.
    /// </summary>
    public ProviderRequest? ProviderRequest { get; }

    /// <summary>
    /// Gets the provider events observed before the failure, if available.
    /// </summary>
    public IReadOnlyList<ProviderEvent>? ProviderEvents { get; }
}
