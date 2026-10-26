namespace WebHook.Core.RequestParameter;

/// <summary>
/// Date range for the time-bound counts on the user dashboard summary.
/// </summary>
public class UserDashboardSummaryAnalyticsRequestParamterDto
{
    /// <summary>The inclusive start date. Defaults to yesterday (UTC).</summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    /// <summary>
    /// The inclusive end date. Defaults to today (UTC), so the default range covers yesterday and today.
    /// </summary>
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
