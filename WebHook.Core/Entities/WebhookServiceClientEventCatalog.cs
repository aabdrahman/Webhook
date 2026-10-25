namespace WebHook.Core.Entities;

/// <summary>
/// Join entity granting a <see cref="WebhookServiceClient"/> permission to publish a
/// <see cref="WebHookEventCatalog"/> event type.
/// </summary>
/// <remarks>
/// The assignment is active while <see cref="DeactivatedAt"/> is <see langword="null"/>.
/// </remarks>
public class WebhookServiceClientEventCatalog
{
    /// <summary>The primary key.</summary>
    public Guid Id { get; set; }
    /// <summary>The service client ID.</summary>
    public Guid ServiceClientId { get; set; }
    /// <summary>The event catalog ID.</summary>
    public Guid EventCatalogId { get; set; }
    /// <summary>When the assignment was made.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>When the assignment was removed, or <see langword="null"/> if it is active.</summary>
    public DateTimeOffset? DeactivatedAt { get; set; }
    /// <summary>The ID of the admin who removed the assignment.</summary>
    public string? DeactivatedBy { get; set; }

    /// <summary>The assigned event type.</summary>
    public WebHookEventCatalog eventCatalog { get; set; }
    /// <summary>The service client.</summary>
    public WebhookServiceClient serviceClient { get; set; }
}
