namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public record WebhookDeliveryProcessorMetadataDto
{
    public Guid DeliveryId { get; set; }
    public string EncryptedSecret { get; set; }
    public string RaisedEventName { get; set; }
    public string? ContactEmail { get; set; } = string.Empty;
    public string ContactName { get; set; }
    public string SubscriptionName { get; set; }
    public double? AverageResponseTime { get; set; }
    public Guid EventId { get; set; }
    public DateTimeOffset? FirstAttemptedAt { get; set; }
    public string? FirstAttemptedResponseCode { get; set; } = string.Empty;
}
