using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class WebhookEvent
{
    public Guid Id { get; set; }
    public string EventType { get; set; }
    public string PayLoad { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Source { get; set; }
    public Guid CorrelationId { get; set; }
    public WebHookEventStatus Status { get; set; }

    //RELATIONSHIP
    //---------One to many relationship with webhook delivery
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}
