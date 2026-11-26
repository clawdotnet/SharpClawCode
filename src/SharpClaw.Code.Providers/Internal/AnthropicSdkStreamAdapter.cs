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

        // message_start carries input/cache usage; message_delta carries cumulative output usage.
        long inputTokens = 0, outputTokens = 0, cacheReadTokens = 0, cacheCreationTokens = 0;
        var sawUsage = false;

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

                if (ev.TryPickStart(out var messageStart))
                {
                    var startUsage = messageStart.Message.Usage;
                    inputTokens = Tokens(startUsage.InputTokens);
                    outputTokens = Tokens(startUsage.OutputTokens);
                    cacheReadTokens = Tokens(startUsage.CacheReadInputTokens);
                    cacheCreationTokens = Tokens(startUsage.CacheCreationInputTokens);
                    sawUsage = true;
                }
                else if (ev.TryPickDelta(out var messageDelta))
                {
                    var deltaUsage = messageDelta.Usage;
                    outputTokens = Math.Max(outputTokens, Tokens(deltaUsage.OutputTokens));
                    inputTokens = Math.Max(inputTokens, Tokens(deltaUsage.InputTokens));
                    cacheReadTokens = Math.Max(cacheReadTokens, Tokens(deltaUsage.CacheReadInputTokens));
                    cacheCreationTokens = Math.Max(cacheCreationTokens, Tokens(deltaUsage.CacheCreationInputTokens));
                    sawUsage = true;
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
                        if (string.IsNullOrWhiteSpace(toolInputJson))
                        {
                            // Zero-argument tool calls stream no input_json_delta; normalize to an empty object.
                            toolInputJson = "{}";
                        }

                        yield return ProviderStreamEventFactory.ToolUse(requestId, clock, pendingToolUseId, pendingToolName, toolInputJson);
                        pendingToolUseId = null;
                        pendingToolName = null;
                        pendingToolInputBuilder = null;
                    }
                }
                else if (ev.TryPickStop(out _))
                {
                    yield return ProviderStreamEventFactory.Completed(requestId, clock, BuildUsage());
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

        yield return ProviderStreamEventFactory.Completed(requestId, clock, BuildUsage());

        UsageSnapshot? BuildUsage()
        {
            if (!sawUsage)
            {
                return null;
            }

            // Anthropic reports cache reads/writes separately from input_tokens; fold them into total input.
            var totalInput = inputTokens + cacheReadTokens + cacheCreationTokens;
            return new UsageSnapshot(
                InputTokens: totalInput,
                OutputTokens: outputTokens,
                CachedInputTokens: cacheReadTokens,
                TotalTokens: totalInput + outputTokens,
                EstimatedCostUsd: null);
        }
    }

    private static long Tokens(long? value) => value ?? 0L;

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
