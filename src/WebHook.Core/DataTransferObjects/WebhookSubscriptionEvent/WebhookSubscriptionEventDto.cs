namespace WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;

/// <summary>
/// A subscription that is subscribed to a given event type.
/// </summary>
public record class WebhookSubscriptionEventDto
{
    /// <summary>The subscription ID.</summary>
    public Guid SubscriptionId { get; init; }

    /// <summary>The subscription name.</summary>
    public string SubscriptionName { get; init; }
}
