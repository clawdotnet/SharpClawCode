using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Models;
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
        UsageSnapshot? usage = null;
        string? finishReason = null;
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
                    cancellationToken.ThrowIfCancellationRequested();
                    streamError = ProviderStreamFailureClassifier.Describe(ex);
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

                if (update.Contents is not null)
                {
                    foreach (var content in update.Contents)
                    {
                        if (content is FunctionCallContent functionCall)
                        {
                            var argsJson = functionCall.Arguments is not null
                                ? System.Text.Json.JsonSerializer.Serialize(functionCall.Arguments)
                                : "{}";
                            yield return ProviderStreamEventFactory.ToolUse(
                                requestId, clock,
                                functionCall.CallId ?? $"call-{Guid.NewGuid():N}",
                                functionCall.Name ?? "unknown",
                                argsJson);
                        }
                    }
                }

                usage = ProviderStreamEventFactory.TryUsageFromUpdate(update) ?? usage;
                finishReason = update.FinishReason?.Value ?? finishReason;
            }
        }
        finally
        {
            if (enumerator is not null)
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        yield return finishReason is "length" or "content_filter"
            ? ProviderStreamEventFactory.Failed(requestId, clock, $"Provider response stopped with {finishReason}; incomplete tool calls were discarded.")
            : ProviderStreamEventFactory.Completed(requestId, clock, usage);
    }
}
