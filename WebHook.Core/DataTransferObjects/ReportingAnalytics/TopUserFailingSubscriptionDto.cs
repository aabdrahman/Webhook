namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

public record class TopUserFailingSubscriptionDto
{
    public string SubscriptionName { get; set; }
    public string CallBackUrl { get; set; }
    public int FailedDeliveryAtttempts { get; set; }
    public int DeadLetterCount { get; set; }
}
