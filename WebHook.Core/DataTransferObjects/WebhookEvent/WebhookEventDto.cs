namespace WebHook.Core.DataTransferObjects.WebhookEvent;

public record class WebhookEventDto
{
    public Guid Id { get; init; }
    public string EventType { get; init; }
    public string PayLoad { get; init; }
    public string Source { get; init; }
    public Guid CorrelationId { get; init; }
    public string Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
}
