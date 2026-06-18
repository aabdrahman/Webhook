namespace WebHook.Core.Entities;

public class WebhookDeliveryAttempt
{
    public Guid Id { get; set; }
    public string HttpResponse { get; set; }
    public string HttpResponseCode { get; set; }
    public long Duration { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public int AttemptedCount { get; set; }

    //RELATIONSHIPS
    //-----------One to many relationship with webhook delivery
    public Guid WebhookDeliveryAttemptId { get; set; }
    public WebhookDeliveryAttempt webhookDeliveryAttempt { get; set; }
}
