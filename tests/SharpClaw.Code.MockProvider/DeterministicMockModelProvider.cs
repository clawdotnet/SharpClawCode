using System.Runtime.CompilerServices;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.MockProvider;

/// <summary>
/// A fully local model provider that streams deterministic content for parity and CI scenarios.
/// </summary>
public sealed class DeterministicMockModelProvider : IModelProvider
{
    private static readonly DateTimeOffset BaseTimestampUtc = new(2026, 4, 6, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Provider id registered in <see cref="SharpClaw.Code.Providers.Configuration.ProviderCatalogOptions"/>.
    /// </summary>
    public const string ProviderNameConstant = "mock";

    /// <summary>
    /// Default synthetic model id used when none is supplied.
    /// </summary>
    public const string DefaultModelId = "deterministic";

    /// <inheritdoc />
    public string ProviderName => ProviderNameConstant;

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(new AuthStatus(
            SubjectId: "mock-subject",
            IsAuthenticated: true,
            ProviderName: ProviderName,
            OrganizationId: null,
            ExpiresAtUtc: null,
            GrantedScopes: ["mock"]));
    }

    /// <inheritdoc />
    public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        var scenario = ResolveScenario(request);
        return Task.FromResult(new ProviderStreamHandle(request, StreamEventsAsync(request, scenario, cancellationToken)));
    }

    private static string ResolveScenario(ProviderRequest request)
    {
        if (request.Metadata is not null
            && request.Metadata.TryGetValue(ParityMetadataKeys.Scenario, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim().ToLowerInvariant();
        }

        return ParityProviderScenario.StreamingText;
    }

    private async IAsyncEnumerable<ProviderEvent> StreamEventsAsync(
        ProviderRequest request,
        string scenario,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        switch (scenario)
        {
            case ParityProviderScenario.StreamFailure:
                throw new InvalidOperationException("Deterministic mock stream failure.");
            case ParityProviderScenario.StreamSlow:
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                yield return CreateDelta(request, sequence: 1, "slow-start");
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
                yield return CreateTerminal(request, sequence: 2);
                yield break;
            case ParityProviderScenario.StreamingText:
            default:
                yield return CreateDelta(request, sequence: 1, "Hello ");
                yield return CreateDelta(request, sequence: 2, "world");
                yield return CreateTerminal(request, sequence: 3);
                yield break;
        }
    }

    private static ProviderEvent CreateDelta(ProviderRequest request, int sequence, string content)
        => new(
            Id: CreateEventId(request, sequence),
            RequestId: request.Id,
            Kind: "text",
            CreatedAtUtc: CreateTimestamp(sequence),
            Content: content,
            IsTerminal: false,
            Usage: null);

    private static ProviderEvent CreateTerminal(ProviderRequest request, int sequence)
        => new(
            Id: CreateEventId(request, sequence),
            RequestId: request.Id,
            Kind: "done",
            CreatedAtUtc: CreateTimestamp(sequence),
            Content: null,
            IsTerminal: true,
            Usage: new UsageSnapshot(1, 2, 0, 3, null));

    private static string CreateEventId(ProviderRequest request, int sequence)
        => $"{request.Id}-evt-{sequence:D2}";

    private static DateTimeOffset CreateTimestamp(int sequence)
        => BaseTimestampUtc.AddMilliseconds(sequence);
}
