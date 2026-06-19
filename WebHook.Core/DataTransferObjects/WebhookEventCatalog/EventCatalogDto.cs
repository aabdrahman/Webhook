namespace WebHook.Core.DataTransferObjects.WebhookEventCatalog;

/// <summary>
/// Represents a webhook event type definition used to expose subscribable events
/// from the event catalog.
/// </summary>
public record class EventCatalogDto
{
    public Guid Id { get; set; }
    public string EventCatalogName { get; init; }
    public string Description { get; init; }
    public List<string> AvailableFields { get; init; }
    public bool IsActive { get; init; }
}