using System;
using System.Collections.Generic;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;

namespace WebHook.Core.Interfaces.Services;

public interface IReportingAnalyticsService
{
    Task<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>> GetMostRecentEventsAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>> GetTopActiveEventCatalogsAsync(CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>> GetTopFailingSubscriptionsAsync(CancellationToken ct = default);
}
