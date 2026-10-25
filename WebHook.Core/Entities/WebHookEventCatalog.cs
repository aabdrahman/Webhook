namespace WebHook.Core.Entities;

/// <summary>
/// Represents a webhook event catalog entry, which defines a subscribable event type
/// that can be registered and used for webhook notifications.
/// </summary>
public class WebHookEventCatalog
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The event type name as entered.</summary>
    public string EventName { get; set; }
    /// <summary>The upper-case event type name used for lookups and matching raised events.</summary>
    public string NormalizedEventName { get; set; }
    /// <summary>Whether the event type can be published and subscribed to.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>A description of when the event is raised.</summary>
    public string Description { get; set; } = string.Empty;
    //public string PayLoad { get; set; } = string.Empty;
    /// <summary>When the event type was registered.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>The payload schema: field names mapped to their type names.</summary>
    public Dictionary<string, string> AvailableFields { get; set; }

    //RELATIONSHIP
    //----Many-to-Many relationship with the webhook subscription
    /// <summary>The subscriptions to this event type.</summary>
    public ICollection<WebhookSubscriptionEvent> WebhookSubscriptions { get; set; } = [];
    //-----One to many relationship with webhook delivery
    //public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];

    //----One to many relationship with the serviceclient join table
    /// <summary>The service clients allowed to publish this event type.</summary>
    public ICollection<WebhookServiceClientEventCatalog> WebhookServiceClients { get; set; } = [];
}
