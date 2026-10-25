namespace WebHook.Core.Entities;

/// <summary>
/// A user's registration of a callback URL to receive one or more event types.
/// </summary>
public class WebhookSubscription
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The subscription name.</summary>
    public string Name { get; set; }
    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the subscription was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <summary>The HTTPS URL deliveries are posted to.</summary>
    public string CallbackUrl { get; set; }
    /// <summary>Whether the subscription receives deliveries.</summary>
    public bool IsActive { get; set; } = true;
    /// <summary>
    /// The AES-encrypted signing secret. It is decrypted at delivery time to compute the
    /// HMAC-SHA256 <c>X-Webhook-Signature</c> header, and regenerated on reactivation.
    /// </summary>
    public string SecretKey { get; set; }
    /// <summary>The payload fields recorded as being of interest.</summary>
    public List<string> SubscribedFields { get; set; } = [];
    /// <summary>The email address for slow-endpoint and dead-letter notifications.</summary>
    public string? ContactEmail { get; set; } = string.Empty;

    //RELATIONSHIPS
    //-----Many to Many relationship with the webhook event catalog
    /// <summary>The event types the subscription receives.</summary>
    public ICollection<WebhookSubscriptionEvent> WebhookEvents { get; set; } = [];
    //------One to many realtionship with the webhook subscription
    //public ICollection<WebhookDelivery> WebhookDeliveries { get; set; } = [];

    //Relationship with teh user - nullable
    /// <summary>The user who created the subscription.</summary>
    public User? CreatedByUser { get; set; }
    /// <summary>The ID of the user who created the subscription.</summary>
    public Guid? CreatedByUserId { get; set; }
}
