namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

/// <summary>
/// One of the current user's subscriptions, ranked by failed delivery attempts, for the user dashboard.
/// </summary>
public record class TopUserFailingSubscriptionDto
{
    /// <summary>The subscription name.</summary>
    public string SubscriptionName { get; set; }
    /// <summary>The callback URL deliveries are posted to.</summary>
    public string CallBackUrl { get; set; }
    /// <summary>The number of delivery attempts that returned a non-2xx response.</summary>
    public int FailedDeliveryAtttempts { get; set; }
    /// <summary>The number of attempts belonging to deliveries that ended in the dead-letter queue.</summary>
    public int DeadLetterCount { get; set; }
}
