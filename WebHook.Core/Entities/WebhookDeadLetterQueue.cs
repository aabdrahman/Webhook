namespace WebHook.Core.Entities;

public class WebhookDeadLetterQueue
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Reason { get; set; }

    //Dead Letter manaual retry fields
    public string? RetriedBy { get; set; }
    public DateTimeOffset? RetriedAt { get; set; }
    public string? RetryJustification { get; set; }

    //RELATIONSHIPS
    //------One to many relationship with webhook delivery
    public Guid WebhookDeliveryId { get; set; }
    public WebhookDelivery webhookDelivery { get; set; }
}