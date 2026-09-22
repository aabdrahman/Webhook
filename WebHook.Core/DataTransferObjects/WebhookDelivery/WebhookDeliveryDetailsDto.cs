namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public record class WebhookDeliveryDetailsDto : WebhookDeliveryDto
{
    public int TotalAttempts { get; set; } = 0;
    public int TotalFailedAttempts { get; set; } = 0;
    public string RequestPayload { get; set; }
    public DateTimeOffset? NextRetryAt { get; set; }
    public string CallBackUrl { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
