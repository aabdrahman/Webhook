using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.WebhookEventCatalog;

/// <summary>
/// Defines the input required to register a new webhook event type in the event catalog.
/// </summary>
public record class CreateEventCatalogDto
{
    [Required(ErrorMessage = "Event Catalog name is a required field")]
    [StringLength(maximumLength: 50, MinimumLength = 0, ErrorMessage = "Event Catalog Name cannot exceed 50 characters")]
    public string EventCatalogName { get; set; }
    public string? Description { get; set; }
    [Range(1, double.MaxValue, ErrorMessage = "One or more Available fields should be provided.")]
    public List<string> AvailableFields { get; set; }
}
