namespace WebHook.Core.Entities;

/// <summary>
/// Represents a webhook event catalog entry, which defines a subscribable event type
/// that can be registered and used for webhook notifications.
/// </summary>
public class WebHookEventCatalog
{
    public Guid Id { get; set; }
    public string EventName { get; set; }
    public string NormalizedEventName { get; set; }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    //public string PayLoad { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Dictionary<string, string> AvailableFields { get; set; }

    //RELATIONSHIP
    //----Many-to-Many relationship with the webhook subscription
    public ICollection<WebhookSubscriptionEvent> WebhookSubscriptions { get; set; } = [];
    //-----One to many relationship with webhook delivery
    //public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}
