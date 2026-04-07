using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Converts Microsoft.Extensions.AI streaming chat updates into SharpClaw <see cref="ProviderEvent"/> values.
/// </summary>
internal static class OpenAiMeaiStreamAdapter
{
    /// <summary>
    /// Adapts an MEAI streaming sequence to provider events (text deltas followed by a single completed event).
    /// </summary>
    public static async IAsyncEnumerable<ProviderEvent> AdaptAsync(
        IAsyncEnumerable<ChatResponseUpdate> updates,
        string requestId,
        ISystemClock clock,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var completed = false;

        await foreach (var update in updates.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var text = update.Text;
            if (!string.IsNullOrEmpty(text))
            {
                yield return ProviderStreamEventFactory.Delta(requestId, clock, text);
            }

            if (update.FinishReason is { } finish && !string.IsNullOrEmpty(finish.Value))
            {
                var usage = ProviderStreamEventFactory.TryUsageFromUpdate(update);
                yield return ProviderStreamEventFactory.Completed(requestId, clock, usage);
                completed = true;
                yield break;
            }
        }

        if (!completed)
        {
            yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
        }
    }
}
