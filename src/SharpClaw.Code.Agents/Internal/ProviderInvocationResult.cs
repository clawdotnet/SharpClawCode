using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Agents.Internal;

internal sealed record ProviderInvocationResult(
    string Output,
    UsageSnapshot Usage,
    string Summary,
    ProviderRequest? ProviderRequest,
    IReadOnlyList<ProviderEvent>? ProviderEvents);
