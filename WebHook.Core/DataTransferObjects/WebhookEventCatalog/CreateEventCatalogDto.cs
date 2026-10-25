using System.ComponentModel.DataAnnotations;
using WebHook.Core.CustomValidators;

namespace WebHook.Core.DataTransferObjects.WebhookEventCatalog;

/// <summary>
/// Defines the input required to register a new webhook event type in the event catalog.
/// </summary>
public record class CreateEventCatalogDto
{
    /// <summary>The event type name, at most 50 characters (for example <c>CUSTOMER_CREATED</c>).</summary>
    [Required(ErrorMessage = "Event Catalog name is a required field")]
    [StringLength(maximumLength: 50, MinimumLength = 0, ErrorMessage = "Event Catalog Name cannot exceed 50 characters")]
    public string EventCatalogName { get; set; }
    /// <summary>An optional description of when the event is raised.</summary>
    public string? Description { get; set; }
    /// <summary>
    /// The payload schema: field names mapped to their type names. Event payloads
    /// published for this type are validated against these fields. At least one is required.
    /// </summary>
    [Required(ErrorMessage = "Available Field is a required field.")]
    [NotEmptyDictionaryCollection]
    //[Range(1, double.MaxValue, ErrorMessage = "One or more Available fields should be provided.")]
    public Dictionary<string, string> AvailableFields { get; set; } = [];
}
