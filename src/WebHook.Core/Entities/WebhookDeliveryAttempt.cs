namespace WebHook.Core.Entities;

/// <summary>
/// The outcome of a single HTTP POST made for a <see cref="WebhookDelivery"/>.
/// </summary>
public class WebhookDeliveryAttempt
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The response body, or the exception message if the request could not be made.</summary>
    public string HttpResponse { get; set; }
    /// <summary>The HTTP status code returned. Codes starting with <c>2</c> count as success.</summary>
    public string HttpResponseCode { get; set; }
    /// <summary>How long the request took, in milliseconds.</summary>
    public double Duration { get; set; }
    /// <summary>When the attempt was made.</summary>
    public DateTimeOffset AttemptedAt { get; set; }
    /// <summary>The number of HTTP calls the record covers. Always 1 at present.</summary>
    public int AttemptedCount { get; set; }

    //RELATIONSHIPS
    //-----------One to many relationship with webhook delivery
    /// <summary>The ID of the delivery the attempt belongs to.</summary>
    public Guid WebhookDeliveryId { get; set; }
    /// <summary>The delivery the attempt belongs to.</summary>
    public WebhookDelivery webhookDelivery { get; set; }
}
