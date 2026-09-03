using System.ComponentModel.DataAnnotations;
using WebHook.Core.CustomValidators;

namespace WebHook.Core.DataTransferObjects.WebhookSubscription;

public record class CreateWebhookSubscriptionDto
{
    [Required(ErrorMessage = "Subscriber Name is a required field.")]
    [Display(Name = "Subscriber Name")]
    public string SubscriberName { get; set; }
    [Required(ErrorMessage = "Kindly provide one or more events to subscribe to.")]
    [NotEmptyCollectionValidator]
    //[Range(1, int.MaxValue, ErrorMessage = "One or more events are to be subscribed to.")]
    public List<string> SubscribedEvents { get; set; }
    [Required(ErrorMessage = "Kindly provide one or more fields to subscribe to.")]
    //[Range(1, int.MaxValue, ErrorMessage = "One or more events are to be subscribed to.")]
    [NotEmptyCollectionValidator]
    public List<string> SubscribedFields { get; set; }
    [Required(ErrorMessage = "Cal Back Url is a required field.")]
    [Display(Name = "Call Back Url")]
    [CallBackUrlValidator]
    public string CallBackUrl { get; set; }
}
