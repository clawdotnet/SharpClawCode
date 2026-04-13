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
        // Track the current tool_use block being accumulated across events.
        string? pendingToolUseId = null;
        string? pendingToolName = null;
        System.Text.StringBuilder? pendingToolInputBuilder = null;

        IAsyncEnumerator<RawMessageStreamEvent>? enumerator = null;
        try
        {
            enumerator = messageStream.GetAsyncEnumerator(cancellationToken);
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

                var ev = enumerator.Current;

                if (ev.TryPickContentBlockStart(out var blockStart))
                {
                    if (blockStart.ContentBlock.TryPickToolUse(out var toolUse))
                    {
                        pendingToolUseId = toolUse.ID;
                        pendingToolName = toolUse.Name;
                        pendingToolInputBuilder = new System.Text.StringBuilder();
                    }
                }
                else if (ev.TryPickContentBlockDelta(out var blockDelta))
                {
                    var (deltaText, partialJson) = ExtractDeltas(blockDelta.Delta);
                    if (!string.IsNullOrEmpty(deltaText))
                    {
                        yield return ProviderStreamEventFactory.Delta(requestId, clock, deltaText);
                    }
                    else if (partialJson is not null && pendingToolInputBuilder is not null)
                    {
                        pendingToolInputBuilder.Append(partialJson);
                    }
                }
                else if (ev.TryPickContentBlockStop(out _))
                {
                    if (pendingToolUseId is not null && pendingToolName is not null && pendingToolInputBuilder is not null)
                    {
                        var toolInputJson = pendingToolInputBuilder.ToString();
                        yield return ProviderStreamEventFactory.ToolUse(requestId, clock, pendingToolUseId, pendingToolName, toolInputJson);
                        pendingToolUseId = null;
                        pendingToolName = null;
                        pendingToolInputBuilder = null;
                    }
                }
                else if (ev.TryPickStop(out _))
                {
                    yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
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

        yield return ProviderStreamEventFactory.Completed(requestId, clock, null);
    }

    private static (string? Text, string? PartialJson) ExtractDeltas(RawContentBlockDelta delta)
    {
        string? text = null;
        string? partialJson = null;

        delta.Match<int>(
            textDelta => { text = textDelta.Text; return 0; },
            inputJsonDelta => { partialJson = inputJsonDelta.PartialJson; return 0; },
            _ => 0,
            _ => 0,
            _ => 0);

        return (text, partialJson);
    }
}
