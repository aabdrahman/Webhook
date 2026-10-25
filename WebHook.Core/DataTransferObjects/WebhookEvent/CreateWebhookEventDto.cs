namespace WebHook.Core.DataTransferObjects.WebhookEvent;

/// <summary>
/// Request from an onboarded service client to raise a webhook event.
/// </summary>
public record class CreateWebhookEventDto
{
    /// <summary>
    /// The event type being raised. It must exist in the event catalog and be
    /// assigned to the calling service client.
    /// </summary>
    public string EventType { get; set; }
    /// <summary>The event payload as a JSON string, validated against the catalog's available fields.</summary>
    public string PayLoad { get; set; }
    /// <summary>The name of the service raising the event.</summary>
    public string Source { get; set; }
    /// <summary>
    /// Groups events from the same business transaction. The combination of correlation ID
    /// and event type must be unique, which prevents the same event being raised twice.
    /// </summary>
    public Guid CorrelationId { get; set; }
}
