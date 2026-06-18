namespace WebHook.Core.Entities;

public class WebHookEventCatalog
{
    public Guid Id { get; set; }
    public string EventName { get; set; }
    public string NormalizedEventName { get; set; }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string PayLoad { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string AvailableFields { get; set; } = string.Empty;

    //RELATIONSHIP
    //----Many-to-Many relationship with the webhook subscription
    public ICollection<WebhookSubscrptionEvent> WebhookSubscriptions { get; set; } = [];
    //-----One to many relationship with webhook delivery
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}
