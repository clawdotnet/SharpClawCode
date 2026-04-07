using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Models;

/// <summary>
/// Represents an active provider stream.
/// </summary>
/// <param name="Request">The normalized request being streamed.</param>
/// <param name="Events">The streamed provider events.</param>
public sealed record ProviderStreamHandle(
    ProviderRequest Request,
    IAsyncEnumerable<ProviderEvent> Events);
