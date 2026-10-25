namespace WebHook.Core.Entities.CustomEntities;

/// <summary>
/// Platform-wide summary for the admin dashboard. Keyless entity mapped to the
/// <c>vw_AdminDashboardSummary</c> database view.
/// </summary>
public class AdminDashboardSummaryReportingAnalyticsEntity
{
    /// <summary>The number of active subscriptions.</summary>
    public int SubscriptionCount { get; set; }
    /// <summary>The number of active event catalog entries.</summary>
    public int ActiveCatalogs { get; set; }
    /// <summary>The number of active service clients.</summary>
    public int TotalServiceClients { get; set; }
    /// <summary>The number of active user accounts.</summary>
    public int TotalOnboardedUsers { get; set; }
    /// <summary>The number of deliveries in <c>Pending</c> status.</summary>
    public int TotalPendingDeliveries { get; set; }
    /// <summary>The number of dead-letter entries that have not been manually retried.</summary>
    public int TotalPendingDeadLetter { get; set; }
    /// <summary>The number of events raised today (UTC calendar day of the database server).</summary>
    public int TotalRaisedEvent { get; set; }
    /// <summary>The percentage (0–100, two decimal places) of all delivery attempts that returned a 2xx response.</summary>
    public double SuccessRate { get; set; }
}
