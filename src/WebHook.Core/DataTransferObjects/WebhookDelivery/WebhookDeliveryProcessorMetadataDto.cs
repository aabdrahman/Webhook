namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

/// <summary>
/// Per-delivery data loaded by the delivery processor and retry services
/// to sign the request and compose escalation emails.
/// </summary>
public record WebhookDeliveryProcessorMetadataDto
{
    /// <summary>The delivery ID.</summary>
    public Guid DeliveryId { get; set; }
    /// <summary>The subscription's encrypted signing secret, decrypted to compute the <c>X-Webhook-Signature</c> header.</summary>
    public string EncryptedSecret { get; set; }
    /// <summary>The event type being delivered, in upper case.</summary>
    public string RaisedEventName { get; set; }
    /// <summary>The subscription's contact email for slow-endpoint and dead-letter notifications, if set.</summary>
    public string? ContactEmail { get; set; } = string.Empty;
    /// <summary>The name used to address the contact in notifications.</summary>
    public string ContactName { get; set; }
    /// <summary>The subscription name.</summary>
    public string SubscriptionName { get; set; }
    /// <summary>The average HTTP duration of previous attempts, in milliseconds.</summary>
    public double? AverageResponseTime { get; set; }
    /// <summary>The ID of the subscription-event link the delivery belongs to.</summary>
    public Guid EventId { get; set; }
    /// <summary>When the first attempt was made, if any.</summary>
    public DateTimeOffset? FirstAttemptedAt { get; set; }
    /// <summary>The HTTP status code of the first attempt, if any.</summary>
    public string? FirstAttemptedResponseCode { get; set; } = string.Empty;
}
