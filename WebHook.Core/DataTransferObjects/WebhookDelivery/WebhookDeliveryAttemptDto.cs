namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public record class WebhookDeliveryAttemptDto
{
    public string HttpResponse { get; set; }
    public string HttpStatusCode { get; set; }
    public double HttpDuration { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
}
