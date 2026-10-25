namespace WebHook.Core.DataTransferObjects.WebhookEvent;

/// <summary>
/// Read model of a raised webhook event.
/// </summary>
public record class WebhookEventDto
{
    /// <summary>The event ID.</summary>
    public Guid Id { get; init; }
    /// <summary>The event type.</summary>
    public string EventType { get; init; }
    /// <summary>The event payload as a JSON string.</summary>
    public string PayLoad { get; init; }
    /// <summary>The name of the service that raised the event.</summary>
    public string Source { get; init; }
    /// <summary>The correlation ID grouping events from the same business transaction.</summary>
    public Guid CorrelationId { get; init; }
    /// <summary>The fan-out status, as the name of a <see cref="Constants.WebHookEventStatus"/> value.</summary>
    public string Status { get; set; }
    /// <summary>When the event was raised.</summary>
    public DateTimeOffset CreatedAt { get; init; }
    /// <summary>When delivery records were created for the event, or <see langword="null"/> if not yet processed.</summary>
    public DateTimeOffset? ProcessedAt { get; init; }
}
