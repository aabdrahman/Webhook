namespace WebHook.Core.DataTransferObjects.WebhookEventCatalog;

/// <summary>
/// Represents a webhook event type definition used to expose subscribable events
/// from the event catalog.
/// </summary>
public record class EventCatalogDto
{
    /// <summary>The event catalog ID.</summary>
    public Guid Id { get; set; }
    /// <summary>The event type name.</summary>
    public string EventCatalogName { get; init; }
    /// <summary>A description of when the event is raised.</summary>
    public string Description { get; init; }
    /// <summary>The payload schema: field names mapped to their type names.</summary>
    public Dictionary<string, string> AvailableFields { get; init; }
    /// <summary>Whether the event type can currently be published and subscribed to.</summary>
    public bool IsActive { get; init; }
}