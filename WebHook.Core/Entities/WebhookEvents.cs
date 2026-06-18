using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

public class WebhookEvents
{
    public Guid Id { get; set; }
    public string EventType { get; set; }
    public string PayLoad { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string Source { get; set; }
    public Guid CorrelationId { get; set; }
    public WebHookEventStatus Status { get; set; }
}
