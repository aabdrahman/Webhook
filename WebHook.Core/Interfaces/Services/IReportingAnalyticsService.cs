using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.RequestParameter;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Provides the figures shown on the admin and user dashboards.
/// </summary>
/// <remarks>
/// The admin methods report on the whole platform. The user methods report only on
/// subscriptions created by the calling user. Unless stated otherwise, each method returns
/// <see cref="System.Net.HttpStatusCode.OK"/> (possibly with empty data) or
/// <see cref="System.Net.HttpStatusCode.InternalServerError"/> on an unexpected error.
/// </remarks>
public interface IReportingAnalyticsService
{
    /// <summary>
    /// Gets the five most recently raised events with their delivery counts, from the <c>vw_getmostrecentevents</c> view.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A <see cref="GenericResponse{T}"/> of up to five <see cref="TopFiveEventAnalyticsEntity"/> rows.</returns>
    Task<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>> GetMostRecentEventsAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the five most-raised active event types, from the <c>vw_GetEventCatalogAnalytics</c> view.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A <see cref="GenericResponse{T}"/> of up to five <see cref="EventCatalogReportingAnalyticsEntity"/> rows.</returns>
    Task<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>> GetTopActiveEventCatalogsAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the five subscriptions with the most failed (non-2xx) delivery attempts, platform-wide.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A <see cref="GenericResponse{T}"/> of up to five <see cref="TopFailingSubscriptionDto"/> rows, most failures first.</returns>
    Task<GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>> GetTopFailingSubscriptionsAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the platform-wide summary counts, from the <c>vw_AdminDashboardSummary</c> view.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>A <see cref="GenericResponse{T}"/> of <see cref="AdminDashboardSummaryReportingAnalyticsEntity"/>.</returns>
    Task<GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>> GetAdminDashboardSummaryAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the calling user's five most recent deliveries.
    /// </summary>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of up to five <see cref="UserDeliveryDashboardDto"/> rows, newest first;
    /// <see cref="System.Net.HttpStatusCode.BadRequest"/> if the caller's user ID claim is invalid.
    /// </returns>
    Task<GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>> GetUserDeliveryDashboardAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the calling user's five subscriptions with the most failed delivery attempts.
    /// </summary>
    /// <remarks>
    /// Rows are grouped by subscription and delivery status, so a subscription can appear more than once.
    /// </remarks>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of up to five <see cref="TopUserFailingSubscriptionDto"/> rows, most failures first;
    /// <see cref="System.Net.HttpStatusCode.BadRequest"/> if the caller's user ID claim is invalid.
    /// </returns>
    Task<GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>> GetUserTopFailingSubscriptionDashboardAsync(CancellationToken ct = default);
    /// <summary>
    /// Gets the calling user's summary counts, from the <c>get_user_dashboard_summary</c> database function.
    /// </summary>
    /// <param name="request">The date range for the time-bound counts; both dates are inclusive.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="UserDashboardSummaryReportAnalyticsEntity"/>;
    /// <see cref="System.Net.HttpStatusCode.BadRequest"/> if the start date is after the end date or the caller's user ID claim is invalid.
    /// </returns>
    Task<GenericResponse<UserDashboardSummaryReportAnalyticsEntity>> GetUserDashboardSummaryAsync(UserDashboardSummaryAnalyticsRequestParamterDto request, CancellationToken ct = default);
}
