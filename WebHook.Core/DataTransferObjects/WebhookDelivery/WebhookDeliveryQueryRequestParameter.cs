using WebHook.Core.Constants;

namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

public class WebhookDeliveryQueryRequestParameter
{
    public int PageSize { get; set; } = 10;
    public int PageNumber { get; set; } = 1;
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public Guid? SubscriptionId { get; set; } = null;
    public WebhookDeliveryStatus? DeliveryStatus { get; set; } = null;
}
