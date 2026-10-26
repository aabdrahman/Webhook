namespace WebHook.Core.Entities;

/// <summary>
/// Join entity linking a <see cref="WebhookSubscription"/> to an event type it receives.
/// Deliveries are created per link.
/// </summary>
public class WebhookSubscriptionEvent
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The subscription ID.</summary>
    public Guid WebhookSubscriptionId { get; set; }
    /// <summary>The event catalog ID.</summary>
    public Guid WebhookEventCatalogId { get; set; }
    /// <summary>When the link was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the link was removed, or <see langword="null"/> if it is active.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>Whether the subscription currently receives this event type.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>The event type.</summary>
    public WebHookEventCatalog webHookEventCatalog { get; set; }
    /// <summary>The subscription.</summary>
    public WebhookSubscription webhookSubscription { get; set; }

    //-----Relationship with the webhookdelivery
    /// <summary>The deliveries created for this link.</summary>
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}