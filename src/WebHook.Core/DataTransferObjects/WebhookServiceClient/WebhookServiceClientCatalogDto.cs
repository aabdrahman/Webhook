namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

/// <summary>
/// An event type assigned to a service client.
/// </summary>
public class WebhookServiceClientCatalogDto
{
    /// <summary>The ID of the client–catalog assignment.</summary>
    public Guid Id { get; set; }
    /// <summary>The service client ID.</summary>
    public Guid ServiceClientId { get; set; }
    /// <summary>The assigned event type name.</summary>
    public string CatalogName { get; set; }
    /// <summary>Whether the assignment is active, allowing the client to publish this event type.</summary>
    public bool IsActive { get; set; }
}