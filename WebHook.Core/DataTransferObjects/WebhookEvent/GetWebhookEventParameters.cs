namespace WebHook.Core.DataTransferObjects.WebhookEvent;

public record class GetWebhookEventParameters
{
    public string? EventType { get; init; }
    public string? Source { get; init; }
    public string? Status { get; init; }
    public Guid? CorrelationId { get; init; }
    public DateTimeOffset? CreatedAtFrom { get; init; } = DateTimeOffset.UtcNow.AddDays(-3);
    public DateTimeOffset? CreatedAtTo { get; init; } = DateTimeOffset.UtcNow;
}