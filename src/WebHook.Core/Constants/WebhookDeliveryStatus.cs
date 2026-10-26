namespace WebHook.Core.Constants;

/// <summary>
/// Lifecycle status of a single <see cref="Entities.WebhookDelivery"/> — one event
/// delivered to one subscription callback URL.
/// </summary>
/// <remarks>
/// Persisted as a string and guarded by a database check constraint.
/// </remarks>
public enum WebhookDeliveryStatus
{
    /// <summary>The delivery is waiting to be claimed by the delivery processor worker.</summary>
    Pending,
    /// <summary>
    /// The delivery has been claimed and locked by a worker and an HTTP attempt is in progress.
    /// A delivery stuck in this state past its lock expiry is released by the stale delivery worker.
    /// </summary>
    Processing,
    /// <summary>The subscriber endpoint acknowledged the delivery with a success status code.</summary>
    Delivered,
    /// <summary>
    /// The last attempt failed (or its worker crashed); the delivery is picked up
    /// again by the retry pending deliveries worker.
    /// </summary>
    Failed,
    /// <summary>
    /// The delivery is scheduled for another attempt.
    /// Reserved: not currently assigned — failed deliveries stay <see cref="Failed"/> until retried.
    /// </summary>
    Retrying,
    /// <summary>
    /// The delivery exhausted its maximum retry count and was moved to the dead-letter queue.
    /// It is only retried again when an admin requests a manual retry.
    /// </summary>
    DeadLetter
}