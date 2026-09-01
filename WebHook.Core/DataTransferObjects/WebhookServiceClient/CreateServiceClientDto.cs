using System.ComponentModel.DataAnnotations;

namespace WebHook.Core.DataTransferObjects.WebhookServiceClient;

public class CreateServiceClientDto
{
    /// <summary>
    /// Human-readable name of the internal service.
    /// Used for display in admin dashboards and audit logs.
    /// Example: Order Service
    /// </summary>
    [Required(ErrorMessage = "Service name is a required field.")]
    [MaxLength(50, ErrorMessage = "Service name canot exceed 50 characters.")]
    public string ServiceName { get; set; }

    /// <summary>
    /// Machine identifier used in the X-Client-Id header on every publish request.
    /// Must be unique, lowercase, alphanumeric with hyphens only.
    /// Cannot be changed after creation.
    /// Example: order-service-prod
    /// </summary>
    [Required(ErrorMessage = "Client Id is a required field.")]
    [MaxLength(50, ErrorMessage = "Client Id cannot exceed 50 characters")]
    [RegularExpression(@"^[a-z0-9]+(-[a-z0-9]+)*$", ErrorMessage = "ClientId must be lowercase alphanumeric with hyphens only. Example: order-service-prod")]
    public string ClientId { get; set; }

    [Required(ErrorMessage = "Contact Email is a required field.")]
    [EmailAddress(ErrorMessage = "Kindly provide a valid email address.")]
    public string ContactEmail { get; set; }

    [MinLength(1, ErrorMessage = "At least one event type must be assigned.")]
    public List<string> AllowedEventTypes { get; set; }
}
