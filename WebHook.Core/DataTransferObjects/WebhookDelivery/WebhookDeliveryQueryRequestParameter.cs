using WebHook.Core.Constants;

namespace WebHook.Core.DataTransferObjects.WebhookDelivery;

/// <summary>
/// Paging and filter parameters for listing deliveries.
/// </summary>
public class WebhookDeliveryQueryRequestParameter
{
    /// <summary>
    /// The maximum number of deliveries to return in a single page.
    /// defaults to 10
    /// </summary>
    public int PageSize { get; set; } = 10;
    /// <summary>
    /// The page number to retrieve.
    /// defaults to 1
    /// </summary>
    public int PageNumber { get; set; } = 1;
    /// <summary>
    /// The inclusive lower boundary for the delivery CreatedAt date.
    /// Defaults to the current date minus three days.
    /// </summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3));
    /// <summary>
    /// The inclusive upper boundary for the delivery CreatedAt date.
    /// Defaults to the current date.
    /// </summary>
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    /// <summary>
    /// Optional identifier of the subscription whose deliveries should be returned.
    /// </summary>
    public Guid? SubscriptionId { get; set; } = null;
    /// <summary>
    /// Optional delivery status used to filter the results.
    /// </summary>
    public WebhookDeliveryStatus? DeliveryStatus { get; set; } = null;
}
