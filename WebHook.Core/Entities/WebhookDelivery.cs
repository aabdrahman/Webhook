using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

/// <summary>
/// One raised event to be delivered to one subscription's callback URL.
/// </summary>
/// <remarks>
/// <para>
/// Deliveries are created by the event fan-out worker, one per active subscription to the
/// event type. Workers claim a delivery by setting <see cref="LockedBy"/> and
/// <see cref="LockedUntil"/> before making an HTTP attempt, so concurrent workers never
/// deliver the same item twice.
/// </para>
/// <para>
/// Each attempt is recorded as a <see cref="WebhookDeliveryAttempt"/>. When the retry budget
/// for the current <see cref="RetryCycle"/> is used up, the delivery is moved to the
/// dead-letter queue (<see cref="WebhookDeadLetterQueue"/>).
/// </para>
/// </remarks>
public class WebhookDelivery
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>When the delivery was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the subscriber acknowledged the delivery, or <see langword="null"/> if it has not.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
    /// <summary>When the next retry is due.</summary>
    public DateTimeOffset? NextRetryAt { get; set; }
    /// <summary>The number of attempts made across all retry cycles.</summary>
    public int RetryCount { get; set; }
    /// <summary>The JSON payload posted to the subscriber.</summary>
    public string RequestPayload { get; set; }
    /// <summary>The current status of the delivery.</summary>
    public WebhookDeliveryStatus DeliveryStatus { get; set; }
    /// <summary>The callback URL, copied from the subscription when the delivery was created.</summary>
    public string CallBackUrl { get; set; }

    //Claim and release properties
    /// <summary>The ID of the worker instance that has claimed the delivery.</summary>
    public string? LockedBy { get; set; }
    /// <summary>When the worker's claim expires. A claim past this time is treated as abandoned.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    //Dead Letter Fields
    /// <summary>
    /// The retry cycle the delivery is in, starting at 1. Each admin manual retry from the
    /// dead-letter queue increments it, granting another full set of automatic retries.
    /// </summary>
    public int RetryCycle { get; set; }

    //RELATIONSHIPS
    //------------One to many relationship with webhook subscription
    //public Guid WebhookSubscriptionId { get; set; }
    //public WebhookSubscription webhookSubscription { get; set; }

    //-----------One to many relationship with webhook event catalog
    //public Guid WebhookEventCatalogId { get; set; }
    //public WebHookEventCatalog webHookEventCatalog { get; set; }

    //----------One to many relationship with webhook event
    /// <summary>The ID of the event being delivered.</summary>
    public Guid WebhookEventId { get; set; }
    /// <summary>The event being delivered.</summary>
    public WebhookEvent webhookEvent { get; set; }

    //----------One to many relationship with webhook subscription event
    /// <summary>The ID of the subscription–event link the delivery was created for.</summary>
    public Guid WebhookSubscriptionEventId { get; set; }
    /// <summary>The subscription–event link the delivery was created for.</summary>
    public WebhookSubscriptionEvent WebhookSubscriptionEvent { get; set; }

    //-----------One to many relationship with delivery attempts
    /// <summary>The HTTP attempts made for the delivery.</summary>
    public ICollection<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; } = [];
    //-----------One to many realtionship with dead letter queue(optional)
    /// <summary>The dead-letter entries recorded for the delivery.</summary>
    public ICollection<WebhookDeadLetterQueue> webhookDeadLetterQueues { get; set; } = [];
}
