using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public int RetryCount { get; set; }
    public string RequestPayload { get; set; }
    public WebhookDeliveryStatus DeliveryStatus { get; set; }

    //RELATIONSHIPS
    //------------One to many relationship with webhook subscription
    public Guid WebhookSubscriptionId { get; set; }
    public WebhookSubscription webhookSubscription { get; set; }

    //-----------One to many relationship with webhook event
    public Guid WebhookEventCatalogId { get; set; }
    public WebHookEventCatalog webHookEventCatalog { get; set; }

    //-----------One to many relationship with delivery attempts
    public ICollection<WebhookDeliveryAttempt> WebhookDeliveryAttempts { get; set; } = [];
    //-----------One to many realtionship with dead letter queue(optional)
    public ICollection<WebhookDeadLetterQueue> webhookDeadLetterQueues { get; set; } = [];
}
