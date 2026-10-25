namespace WebHook.Core.Constants;

/// <summary>
/// Lifecycle status of a raised <see cref="Entities.WebhookEvent"/> as it is fanned out
/// into per-subscriber deliveries.
/// </summary>
/// <remarks>
/// Persisted as a string and guarded by a database check constraint.
/// </remarks>
public enum WebHookEventStatus
{
    /// <summary>
    /// The event has been persisted but no delivery records have been created yet.
    /// Events left in this state past the configured threshold are re-queued by the
    /// pending raised events worker.
    /// </summary>
    Pending,
    /// <summary>
    /// Delivery records have been created for every active subscription to the event type.
    /// The event stays in this status; the outcome of each delivery is tracked on the delivery.
    /// </summary>
    Processing,
    /// <summary>The event was fanned out but had no active subscribers, so no deliveries were created.</summary>
    Processed,
    /// <summary>
    /// Delivery records were created for only some of the matching subscriptions.
    /// Reserved: not currently assigned by the fan-out worker.
    /// </summary>
    PartiallyProcessed,
    /// <summary>
    /// The event could not be fanned out.
    /// Reserved: not currently assigned by the fan-out worker.
    /// </summary>
    Failed
}
