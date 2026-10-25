namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

/// <summary>
/// Summary of a delivery — one event delivered to one subscription — for list views.
/// </summary>
public record class WebhookDeliveryDto
{
    /// <summary>The delivery ID.</summary>
    public Guid Id { get; set; }
    /// <summary>The name of the receiving subscription.</summary>
    public string SubscriberName { get; set; }
    /// <summary>The event type being delivered.</summary>
    public string EventType { get; set; }
    /// <summary>The delivery status, as the name of a <see cref="Constants.WebhookDeliveryStatus"/> value.</summary>
    public string Status { get; set; }
    /// <summary>
    /// The retry cycle the delivery is in. Each admin manual retry from the dead-letter
    /// queue starts a new cycle, allowing another full set of automatic retries.
    /// </summary>
    public int RetryCycle { get; set; }
    /// <summary>The average HTTP duration of the delivery's attempts, in milliseconds.</summary>
    public double? AverageDuration { get; set; } = 0;
    /// <summary>The number of attempts made across all retry cycles.</summary>
    public int RetryCount { get; set; }
    /// <summary>When the delivery was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
