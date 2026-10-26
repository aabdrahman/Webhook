using WebHook.Core.Constants;

namespace WebHook.Core.Entities;

/// <summary>
/// An occurrence of a catalogued event type, raised by a service client.
/// </summary>
/// <remarks>
/// The catalog (<see cref="WebHookEventCatalog"/>) defines what can happen; a
/// <see cref="WebhookEvent"/> records what did happen. Each event is fanned out
/// into one <see cref="WebhookDelivery"/> per active subscription to its type.
/// </remarks>
public class WebhookEvent
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The normalised event type name.</summary>
    public string EventType { get; set; }
    /// <summary>The event payload as a JSON string.</summary>
    public string PayLoad { get; set; }
    /// <summary>When the event was raised.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the event was fanned out into deliveries, or <see langword="null"/> if not yet.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
    /// <summary>The name of the service that raised the event.</summary>
    public string Source { get; set; }
    /// <summary>Groups events from the same business transaction. Unique together with <see cref="EventType"/>.</summary>
    public Guid CorrelationId { get; set; }
    /// <summary>The fan-out status of the event.</summary>
    public WebHookEventStatus Status { get; set; }

    //RELATIONSHIP
    //---------One to many relationship with webhook delivery
    /// <summary>The deliveries created for the event.</summary>
    public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];
}
