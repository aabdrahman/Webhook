namespace WebHook.Core.Entities;

public class WebhookServiceClientEventCatalog
{
    public Guid Id { get; set; }
    public Guid ServiceClientId { get; set; }
    public Guid EventCatalogId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeactivatedAt { get; set; }
    public string? DeactivatedBy { get; set; }

    public WebHookEventCatalog eventCatalog { get; set; }
    public WebhookServiceClient serviceClient { get; set; }
}
