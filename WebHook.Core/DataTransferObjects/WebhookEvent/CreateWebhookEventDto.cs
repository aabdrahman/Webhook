namespace WebHook.Core.DataTransferObjects.WebhookEvent;

public record class CreateWebhookEventDto
{
    public string EventType { get; set; }
    public string PayLoad { get; set; }
    public string Source { get; set; }
    public Guid CorrelationId { get; set; }
}
