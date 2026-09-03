namespace WebHook.Core.Entities;

public class WebhookSubscriptionEvent
{
    public Guid Id { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid WebhookEventCatalogId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public WebHookEventCatalog webHookEventCatalog { get; set; }
    public WebhookSubscription webhookSubscription { get; set; }

    //-----Relationship with the webhookdelivery
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}