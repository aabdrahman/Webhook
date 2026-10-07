namespace WebHook.Core.RequestParameter;

public class UserDashboardSummaryAnalyticsRequestParamterDto
{
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    public DateOnly EndDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}
