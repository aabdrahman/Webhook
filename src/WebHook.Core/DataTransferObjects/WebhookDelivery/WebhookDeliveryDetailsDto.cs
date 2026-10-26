namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

/// <summary>
/// Full details of a single delivery, extending the summary in <see cref="WebhookDeliveryDto"/>.
/// </summary>
public record class WebhookDeliveryDetailsDto : WebhookDeliveryDto
{
    /// <summary>The total number of HTTP attempts made for the delivery.</summary>
    public int TotalAttempts { get; set; } = 0;
    /// <summary>The number of attempts considered failed.</summary>
    public int TotalFailedAttempts { get; set; } = 0;
    /// <summary>The JSON payload posted to the subscriber.</summary>
    public string RequestPayload { get; set; }
    /// <summary>When the next retry is due, if the delivery is awaiting retry.</summary>
    public DateTimeOffset? NextRetryAt { get; set; }
    /// <summary>The subscriber callback URL the delivery is posted to.</summary>
    public string CallBackUrl { get; set; }
    /// <summary>When the delivery succeeded, or <see langword="null"/> if it has not.</summary>
    public DateTimeOffset? DeliveredAt { get; set; }
}
