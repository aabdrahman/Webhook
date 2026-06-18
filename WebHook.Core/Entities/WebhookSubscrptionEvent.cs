namespace WebHook.Core.Entities;

public class WebhookSubscrptionEvent
{
    public Guid Id { get; set; }
    public Guid WebhookSubscriptionId { get; set; }
    public Guid WebhookEventCatalogId { get; set; }
    public WebHookEventCatalog webHookEventCatalog { get; set; }
    public WebhookSubscription webhookSubscription { get; set; }
}