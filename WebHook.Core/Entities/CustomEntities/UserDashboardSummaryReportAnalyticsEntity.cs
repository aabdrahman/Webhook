namespace WebHook.Core.Entities.CustomEntities;

/// <summary>
/// Summary of one user's subscriptions and deliveries for the user dashboard. Keyless entity
/// returned by the <c>get_user_dashboard_summary</c> database function.
/// </summary>
/// <remarks>
/// The "today" counts cover the requested date range (both dates inclusive).
/// </remarks>
public class UserDashboardSummaryReportAnalyticsEntity
{
    /// <summary>
    /// The percentage (0–100, two decimal places) of the user's delivery attempts that returned
    /// a 2xx response; <see langword="null"/> if there were no attempts.
    /// </summary>
    public double? SuccessRate { get; set; }
    /// <summary>The total number of deliveries to the user's subscriptions.</summary>
    public int TotalDeliveries { get; set; }
    /// <summary>The number of the user's subscriptions that are active.</summary>
    public int ActiveSubscriptions { get; set; }
    /// <summary>The number of distinct event types the user is actively subscribed to.</summary>
    public int ActiveSubscribedCatalogs { get; set; }
    /// <summary>The number of the user's deliveries in <c>Pending</c> status.</summary>
    public int PendingDeliveries { get; set; }
    /// <summary>The number of pending deliveries created within the date range.</summary>
    public int PendingDeliveriesToday { get; set; }
    /// <summary>The total number of the user's subscriptions.</summary>
    public int TotalSubscription { get; set; }
    /// <summary>The number of dead-letter entries for the user's deliveries.</summary>
    public int TotalDeadLetter { get; set; }
    /// <summary>The number of dead-letter entries manually retried within the date range.</summary>
    public int DeadLetterRetriedToday { get; set; }
}

