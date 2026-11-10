using System.Runtime.CompilerServices;
using Anthropic.Models.Messages;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Models;
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

        long inputTokens = 0, outputTokens = 0, cachedTokens = 0, createdCacheTokens = 0;
        var tools = new List<ProviderEvent>();
        string? stopReason = null;
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

                var ev = enumerator.Current;

                if (ev.TryPickStart(out var start))
                {
                    inputTokens = start.Message.Usage.InputTokens;
                    createdCacheTokens = start.Message.Usage.CacheCreationInputTokens ?? 0;
                    outputTokens = start.Message.Usage.OutputTokens;
                    cachedTokens = start.Message.Usage.CacheReadInputTokens ?? 0;
                }
                else if (ev.TryPickDelta(out var delta))
                {
                    inputTokens = delta.Usage.InputTokens ?? inputTokens;
                    createdCacheTokens = delta.Usage.CacheCreationInputTokens ?? createdCacheTokens;
                    outputTokens = delta.Usage.OutputTokens;
                    cachedTokens = delta.Usage.CacheReadInputTokens ?? cachedTokens;
                    // Read the wire value of the SDK's extensible enum.
                    var json = System.Text.Json.JsonSerializer.SerializeToElement(delta.Delta);
                    if (json.TryGetProperty("stop_reason", out var reason) && reason.ValueKind == System.Text.Json.JsonValueKind.String)
                        stopReason = reason.GetString();
                }
                else if (ev.TryPickContentBlockStart(out var blockStart))
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
                        tools.Add(ProviderStreamEventFactory.ToolUse(requestId, clock, pendingToolUseId, pendingToolName, string.IsNullOrEmpty(toolInputJson) ? "{}" : toolInputJson));
                        pendingToolUseId = null;
                        pendingToolName = null;
                        pendingToolInputBuilder = null;
                    }
                }
                else if (ev.TryPickStop(out _))
                {
                    if (stopReason is not ("end_turn" or "tool_use" or "stop_sequence") || pendingToolUseId is not null)
                    {
                        yield return ProviderStreamEventFactory.Failed(requestId, clock, $"Anthropic response is incomplete (stop reason: {stopReason ?? "missing"}); tool calls were discarded.");
                        yield break;
                    }
                    foreach (var tool in tools) yield return tool;
                    yield return ProviderStreamEventFactory.Completed(requestId, clock,
                        new UsageSnapshot(inputTokens + createdCacheTokens + cachedTokens, outputTokens, cachedTokens, inputTokens + createdCacheTokens + cachedTokens + outputTokens, null));
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

        yield return ProviderStreamEventFactory.Failed(requestId, clock, "Anthropic stream ended before message_stop.");
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
