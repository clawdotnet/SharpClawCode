using SharpClaw.Code.Protocol.Events;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Lightweight fan-out for <see cref="RuntimeEvent" />: ring buffer (replay/debug), optional session persistence, usage aggregation hooks.
/// </summary>
public interface IRuntimeEventPublisher
{
    /// <summary>
    /// Publishes a runtime event. Swallows persistence failures after logging; never throws for valid events.
    /// </summary>
    /// <param name="runtimeEvent">The event to publish.</param>
    /// <param name="options">Routing options; omit or disable persistence for catalog-only emissions.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask PublishAsync(RuntimeEvent runtimeEvent, RuntimeEventPublishOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes several events in order with the same routing. Implementations should persist them in one store operation;
    /// observable results (ring buffer, usage, sink order) match publishing each event individually.
    /// </summary>
    /// <param name="runtimeEvents">The events to publish, in order.</param>
    /// <param name="options">Routing options shared by every event.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    async ValueTask PublishRangeAsync(IReadOnlyList<RuntimeEvent> runtimeEvents, RuntimeEventPublishOptions? options = null, CancellationToken cancellationToken = default)
    {
        foreach (var runtimeEvent in runtimeEvents)
        {
            await PublishAsync(runtimeEvent, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns a snapshot of recent events retained in the in-process buffer (for export or debugging).
    /// </summary>
    /// <returns>A copy ordered from oldest to newest within the buffer window.</returns>
    IReadOnlyList<RuntimeEvent> GetRecentEventsSnapshot();
}
