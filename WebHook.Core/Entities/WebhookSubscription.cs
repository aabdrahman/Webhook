namespace WebHook.Core.Entities;

public class WebhookSubscription
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string CallbackUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public string SecretKey { get; set; }
    public List<string> SubscribedFields { get; set; } = [];

    //RELATIONSHIPS
    //-----Many to Many relationship with the webhook event catalog
    public ICollection<WebhookSubscriptionEvent> WebhookEvents { get; set; } = [];
    //------One to many realtionship with the webhook subscription
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}
