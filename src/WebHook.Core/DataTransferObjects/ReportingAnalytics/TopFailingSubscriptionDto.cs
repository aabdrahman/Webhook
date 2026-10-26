namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

/// <summary>
/// A subscription ranked by failed delivery attempts, platform-wide, for the admin dashboard.
/// </summary>
public class TopFailingSubscriptionDto
{
    /// <summary>The subscription name.</summary>
    public string SubscriptionName { get; set; }
    /// <summary>The callback URL deliveries are posted to.</summary>
    public string CallBackUrl { get; set; }
    /// <summary>The number of delivery attempts that returned a non-2xx response.</summary>
    public int FailedCount { get; set; }
    /// <summary>The total number of delivery attempts.</summary>
    public int TotalCount { get; set; }
}
