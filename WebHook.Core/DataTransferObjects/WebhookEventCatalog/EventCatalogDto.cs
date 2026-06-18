namespace WebHook.Core.DataTransferObjects.WebhookEventCatalog;

public record class EventCatalogDto
{
    public Guid Id { get; set; }
    public string EventCatalogName { get; init; }
    public string Description { get; init; }
    public List<string> AvailableFields { get; init; }
    public bool IsActive { get; init; }
}