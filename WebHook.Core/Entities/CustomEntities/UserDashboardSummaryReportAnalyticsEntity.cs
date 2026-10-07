namespace WebHook.Core.Entities.CustomEntities;

public class UserDashboardSummaryReportAnalyticsEntity
{
    public double? SuccessRate { get; set; }
    public int TotalDeliveries { get; set; }
    public int ActiveSubscriptions { get; set; }
    public int ActiveSubscribedCatalogs { get; set; }
    public int PendingDeliveries { get; set; }
    public int PendingDeliveriesToday { get; set; }
    public int TotalSubscription { get; set; }
    public int TotalDeadLetter { get; set; }
    public int DeadLetterRetriedToday { get; set; }
}

