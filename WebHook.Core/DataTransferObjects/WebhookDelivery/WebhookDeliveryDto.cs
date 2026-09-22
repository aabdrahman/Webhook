namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public record class WebhookDeliveryDto
{
    public Guid Id { get; set; }
    public string SubscriberName { get; set; }
    public string EventType { get; set; }
    public string Status { get; set; }
    public int RetryCycle { get; set; }
    public double AverageDuration { get; set; } = 0;
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
