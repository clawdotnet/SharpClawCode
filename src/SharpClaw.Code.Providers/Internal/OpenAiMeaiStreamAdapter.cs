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
        IAsyncEnumerator<ChatResponseUpdate>? enumerator = null;
        try
        {
            enumerator = updates.GetAsyncEnumerator(cancellationToken);
            while (true)
            {
                bool moved;
                string? streamError = null;
                try
                {
                    moved = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    streamError = ex.Message;
                    moved = false;
                }

                if (streamError is not null)
                {
                    yield return ProviderStreamEventFactory.Failed(requestId, clock, streamError);
                    yield break;
                }

                if (!moved)
                {
                    break;
                }

                var update = enumerator.Current;
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
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        if (!completed)
        {
            yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
        }
    }
}
