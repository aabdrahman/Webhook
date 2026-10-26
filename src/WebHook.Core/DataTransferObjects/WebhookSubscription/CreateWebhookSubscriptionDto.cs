using System.ComponentModel.DataAnnotations;
using WebHook.Core.CustomValidators;

namespace WebHook.Core.DataTransferObjects.WebhookSubscription;

/// <summary>
/// A user's request to register a callback URL for one or more event types.
/// </summary>
public record class CreateWebhookSubscriptionDto
{
    /// <summary>A name for the subscription.</summary>
    [Required(ErrorMessage = "Subscriber Name is a required field.")]
    [Display(Name = "Subscriber Name")]
    public string SubscriberName { get; set; }
    /// <summary>The event type names to subscribe to. At least one is required, and each must exist in the catalog.</summary>
    [Required(ErrorMessage = "Kindly provide one or more events to subscribe to.")]
    [NotEmptyCollectionValidator]
    //[Range(1, int.MaxValue, ErrorMessage = "One or more events are to be subscribed to.")]
    public List<string> SubscribedEvents { get; set; } = [];
    //[Required(ErrorMessage = "Kindly provide one or more fields to subscribe to.")]
    //[Range(1, int.MaxValue, ErrorMessage = "One or more events are to be subscribed to.")]
    //[NotEmptyCollectionValidator]
    /// <summary>Optional list of payload fields of interest. Stored with the subscription; not used to filter payloads.</summary>
    public List<string> SubscribedFields { get; set; } = [];
    /// <summary>The absolute HTTPS URL deliveries are posted to. Loopback addresses are rejected.</summary>
    [Required(ErrorMessage = "Cal Back Url is a required field.")]
    [Display(Name = "Call Back Url")]
    [CallBackUrlValidator]
    public string CallBackUrl { get; set; }
    /// <summary>
    /// The email address for slow-endpoint and dead-letter notifications.
    /// Defaults to the creating user's email address.
    /// </summary>
    [EmailAddress]
    //[Required(AllowEmptyStrings = true,)]
    public string? ContactEmail { get; set; } = null;
}
