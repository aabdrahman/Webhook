namespace WebHook.Core.DataTransferObjects.ReportingAnalytics;

public class TopFailingSubscriptionDto
{
    public string SubscriptionName { get; set; }
    public string CallBackUrl { get; set; }
    public int FailedCount { get; set; }
    public int TotalCount { get; set; }
}
