namespace WebHook.Core.Entities;

public class WebhookSubscriptionEvent
{
    public Guid Id { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid WebhookEventCatalogId { get; set; }
    public WebHookEventCatalog webHookEventCatalog { get; set; }
    public WebhookSubscription webhookSubscription { get; set; }
}