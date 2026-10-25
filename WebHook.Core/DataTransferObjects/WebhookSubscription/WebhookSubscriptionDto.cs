namespace WebHook.Core.DataTransferObjects.WebhookSubscription;

/// <summary>
/// Read model of a webhook subscription.
/// </summary>
public record class WebhookSubscriptionDto
{
    /// <summary>The subscription ID.</summary>
    public Guid Id { get; set; }
    /// <summary>The subscription name.</summary>
    public string Name { get; set; }
    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset CreatedDate { get; set; }
    /// <summary>
    /// The plaintext signing secret used to verify <c>X-Webhook-Signature</c>. Returned only when a
    /// single subscription is fetched by ID; empty in list responses.
    /// </summary>
    public string SecretKey { get; set; }
    /// <summary>The callback URL deliveries are posted to.</summary>
    public string CallBackUrl { get; set; }
    /// <summary>Whether the subscription is active and receiving deliveries.</summary>
    public bool IsActive { get; set; }
    /// <summary>The payload fields recorded as being of interest.</summary>
    public IReadOnlyList<string> SubscribedFields { get; set; } = [];
    /// <summary>The normalised names of the event types the subscription receives.</summary>
    public IReadOnlyList<string> SubscribedEvents { get; set; } = [];
}
