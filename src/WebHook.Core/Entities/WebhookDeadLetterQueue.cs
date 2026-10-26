namespace WebHook.Core.Entities;

/// <summary>
/// Records a delivery being moved to the dead-letter queue after exhausting its retries,
/// and any manual retry an admin requests for it.
/// </summary>
/// <remarks>
/// A delivery gets one entry each time it is dead-lettered, so a delivery retried
/// manually and dead-lettered again has several entries.
/// </remarks>
public class WebhookDeadLetterQueue
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>When the delivery was dead-lettered.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Why the delivery was dead-lettered.</summary>
    public string Reason { get; set; }

    //Dead Letter manaual retry fields
    /// <summary>The ID of the admin who requested a manual retry.</summary>
    public string? RetriedBy { get; set; }
    /// <summary>When a manual retry was requested, or <see langword="null"/> if none has been.</summary>
    public DateTimeOffset? RetriedAt { get; set; }
    /// <summary>The justification the admin gave for the manual retry.</summary>
    public string? RetryJustification { get; set; }

    //RELATIONSHIPS
    //------One to many relationship with webhook delivery
    /// <summary>The ID of the dead-lettered delivery.</summary>
    public Guid WebhookDeliveryId { get; set; }
    /// <summary>The dead-lettered delivery.</summary>
    public WebhookDelivery webhookDelivery { get; set; }
}