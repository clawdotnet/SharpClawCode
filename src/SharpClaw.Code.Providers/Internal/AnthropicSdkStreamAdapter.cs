using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Converts Anthropic SDK <see cref="RawMessageStreamEvent"/> sequences into SharpClaw <see cref="ProviderEvent"/> values.
/// </summary>
internal static class AnthropicSdkStreamAdapter
{
    /// <summary>
    /// Adapts a raw Anthropic message stream into provider events.
    /// </summary>
    public static async IAsyncEnumerable<ProviderEvent> AdaptAsync(
        IAsyncEnumerable<RawMessageStreamEvent> messageStream,
        string requestId,
        ISystemClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var ev in messageStream.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (ev.TryPickContentBlockDelta(out var blockDelta))
            {
                var deltaText = ExtractTextDelta(blockDelta.Delta);
                if (!string.IsNullOrEmpty(deltaText))
                {
                    yield return ProviderStreamEventFactory.Delta(requestId, clock, deltaText);
                }
            }
            else if (ev.TryPickStop(out _))
            {
                yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
                yield break;
            }
        }

        yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
    }

    private static string? ExtractTextDelta(RawContentBlockDelta delta)
        => delta.Match<string?>(
            text => text.Text,
            _ => null,
            _ => null,
            _ => null,
            _ => null);
}
