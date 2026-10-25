namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

/// <summary>
/// A single HTTP attempt to deliver a webhook to a subscriber.
/// </summary>
public record class WebhookDeliveryAttemptDto
{
    /// <summary>The response body returned by the subscriber endpoint, or the error message if the request failed.</summary>
    public string HttpResponse { get; set; }
    /// <summary>The HTTP status code returned by the subscriber endpoint.</summary>
    public string HttpStatusCode { get; set; }
    /// <summary>How long the HTTP request took, in milliseconds.</summary>
    public double HttpDuration { get; set; }
    /// <summary>When the attempt was made.</summary>
    public DateTimeOffset AttemptedAt { get; set; }
}
