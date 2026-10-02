using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class ReportingAnalyticsService : IReportingAnalyticsService
{
    private readonly RepositoryContext _repositoryContext;

    public ReportingAnalyticsService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(ReportingAnalyticsService));

    public async Task<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>> GetMostRecentEventsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetMostRecentEventsAsync));
        try
        {
            _logger.Information("Fetching top five most recent events...");

            List<TopFiveEventAnalyticsEntity> topFiveEvents = await _repositoryContext.TopFiveEventAnalyticsEntities.ToListAsync(ct);

            _logger.Information("Top five events fetched successfully - {0}", topFiveEvents);

            return GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>.Success(topFiveEvents, "Most recent events fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching top five most recent events...");
            return GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>.Failure(null, "An error occurred while fetching top five most recent events.", HttpStatusCode.InternalServerError, new ErrorDetail { ErrorMessage = ex.Message });
        }

    }

    public async Task<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>> GetTopActiveEventCatalogsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetTopActiveEventCatalogsAsync));
        try
        {
            _logger.Information("Getting top five active event catalogs...");

            List<EventCatalogReportingAnalyticsEntity> items = await _repositoryContext.EventCatalogReportingAnalyticsEntities.ToListAsync(ct);

            _logger.Information("Top five event catalogs fetched successfully - {0}", items);

            return GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>.Success(items, "Top active event catalogs fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching event catalogs analytics.");
            return GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>.Failure(null, "An error occurred while fetching event catalogs analytics.", HttpStatusCode.InternalServerError, new ErrorDetail { ErrorMessage = ex.Message });
        }
    }

    public async Task<GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>> GetTopFailingSubscriptionsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetTopFailingSubscriptionsAsync));
        try
        {
            _logger.Information("Fetching top failing subscriptions...");
            //var topFailingSubscriptions = await _repositoryContext.WebhookDeliveryAttempts
            //        .GroupBy(w => new { w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.Name, w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.CallbackUrl })
            //        .Select(g => new TopFailingSubscriptionDto
            //        {
            //            SubscriptionName = g.Key.Name,
            //            CallBackUrl = g.Key.CallbackUrl,
            //            FailedCount = g.Count(x => !x.HttpResponseCode.StartsWith('2'))
            //        })
            //        .OrderByDescending(s => s.FailedCount)
            //        .Take(5)
            //        .ToListAsync(ct);
            var topFailingSubscriptions = await _repositoryContext.WebhookDeliveryAttempts
                    .Select(w => new
                    {
                        SubscriptionName = w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.Name,
                        CallbackUrl = w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.CallbackUrl,
                        IsFailure = !w.HttpResponseCode.StartsWith("2")
                    })
                    .GroupBy(w => new { w.SubscriptionName, w.CallbackUrl })
                    .Select(g => new TopFailingSubscriptionDto
                    {
                        SubscriptionName = g.Key.SubscriptionName,
                        CallBackUrl = g.Key.CallbackUrl,
                        FailedCount = g.Count(x => x.IsFailure),
                        TotalCount = g.Count()
                    })
                    .OrderByDescending(s => s.FailedCount)
                    .Take(5)
                    .ToListAsync(ct);

            _logger.Information("Top failing subscriptions fetched successfully - {0}", topFailingSubscriptions);

            return GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>.Success(topFailingSubscriptions, "Top failing subscriptions fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching top failing subscriptions.");
            return GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>.Failure(null, "An error occurred while fetching top failing subscriptions.", HttpStatusCode.InternalServerError, new ErrorDetail { ErrorMessage = ex.Message });
        }
    }
}
