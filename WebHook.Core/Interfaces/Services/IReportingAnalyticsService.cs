using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.RequestParameter;

namespace WebHook.Core.Interfaces.Services;

public interface IReportingAnalyticsService
{
    Task<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>> GetMostRecentEventsAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>> GetTopActiveEventCatalogsAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>> GetTopFailingSubscriptionsAsync(CancellationToken ct = default);
    Task<GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>> GetAdminDashboardSummaryAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>> GetUserDeliveryDashboardAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>> GetUserTopFailingSubscriptionDashboardAsync(CancellationToken ct = default);
    Task<GenericResponse<UserDashboardSummaryReportAnalyticsEntity>> GetUserDashboardSummaryAsync(UserDashboardSummaryAnalyticsRequestParamterDto request, CancellationToken ct = default);
}
