namespace WebHook.Core.Entities.CustomEntities;

public class AdminDashboardSummaryReportingAnalyticsEntity
{
    public int SubscriptionCount { get; set; }
    public int ActiveCatalogs { get; set; }
    public int TotalServiceClients { get; set; }
    public int TotalOnboardedUsers { get; set; }
    public int TotalPendingDeliveries { get; set; }
    public int TotalPendingDeadLetter { get; set; }
    public int TotalRaisedEvent { get; set; }
    public double SuccessRate { get; set; }
}
