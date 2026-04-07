namespace SharpClaw.Code.MockProvider;

/// <summary>
/// Named scenarios implemented by <see cref="DeterministicMockModelProvider"/>.
/// </summary>
public static class ParityProviderScenario
{
    /// <summary>
    /// Streams two text deltas followed by a terminal event.
    /// </summary>
    public const string StreamingText = "streaming_text";

    /// <summary>
    /// Throws during stream enumeration to exercise provider failure handling.
    /// </summary>
    public const string StreamFailure = "stream_failure";

    /// <summary>
    /// Delays long enough to let timeout and recovery scenarios cancel the stream.
    /// </summary>
    public const string StreamSlow = "stream_slow";
}
