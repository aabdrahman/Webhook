using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class ReportingAnalyticsService : IReportingAnalyticsService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    public ReportingAnalyticsService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
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

    public async Task<GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>> GetAdminDashboardSummaryAsync(CancellationToken ct = default)
    {
        _logger.ForContext(_methodName, nameof(GetAdminDashboardSummaryAsync));

        try
        {
            _logger.Information("Fetching Admin dashboard summary...");

            var dashboard = await _repositoryContext.AdminDashboardSummaryReportingAnalyticsEntities.FirstOrDefaultAsync(ct);

            _logger.Information("Admin dashboard summary fetched successfully - {0}", dashboard);

            return GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>.Success(dashboard, "Admin dashboard summary fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting admin dashboard summary.");
            return GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>.Failure(null, "An error occurred while getting admin dashboard summary.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorMessage = ex.Message });
        }

    }

    public async Task<GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>> GetUserDeliveryDashboardAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetUserDeliveryDashboardAsync));
        try
        {
            _logger.Information("Getting dashboard summary for user - {0}, {1}", _authenticatedUserDetails.userId, _authenticatedUserDetails.emailAddress);

            if(!Guid.TryParse(_authenticatedUserDetails.userId, out var userId))
            {
                _logger.Warning("Authenticated user details could not be parsed successfully - {0}", _authenticatedUserDetails.userId);
                return GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>.Failure(null, "User dashboard could not be fetched.", HttpStatusCode.BadRequest);
            }

            var userDeliveryDashboard = await _repositoryContext.WebhookDeliveries
                                                    .Where(x => x.WebhookSubscriptionEvent.webhookSubscription.CreatedByUserId == userId)
                                                    .Select(x => new UserDeliveryDashboardDto()
                                                    {
                                                        Status = x.DeliveryStatus.ToString(),
                                                        SubscriptionName = x.WebhookSubscriptionEvent.webhookSubscription.Name,
                                                        AverageDuration = x.WebhookDeliveryAttempts.Average(x => x.Duration),
                                                        CreatedAt = x.CreatedAt,
                                                        EventType = x.webhookEvent.EventType
                                                    }).OrderByDescending(x => x.CreatedAt)
                                                    .Take(5)
                                                    .ToListAsync(ct);

            _logger.Information("User delivery dashboard fetched successfully for user: {0} - {1}", _authenticatedUserDetails.emailAddress, userDeliveryDashboard);
            return GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>.Success(userDeliveryDashboard, "User delivery dashboard fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occcurred while getting delivery dashboard summary for user.");
            return GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>.Failure(null, "An error occurred while fetching user delivery dashboard.", HttpStatusCode.InternalServerError);
        }
    }

    public async Task<GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>> GetUserTopFailingSubscriptionDashboardAsync(CancellationToken ct = default)
    {
        _logger.ForContext(_methodName, nameof(GetUserTopFailingSubscriptionDashboardAsync));
        try
        {
            _logger.Information("Fetching top failing subscription for user - {0},{1}", _authenticatedUserDetails.userId, _authenticatedUserDetails.emailAddress);

            if (!Guid.TryParse(_authenticatedUserDetails.userId, out var userId))
            {
                _logger.Warning("Authenticated user details could not be parsed successfully - {0}", _authenticatedUserDetails.userId);
                return GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>.Failure(null, "User dashboard could not be fetched.", HttpStatusCode.BadRequest);
            }

            var topFailingSubscriptions = await _repositoryContext.WebhookDeliveryAttempts
                    .Where(x => x.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.CreatedByUserId == userId)
                    .Select(w => new
                    {
                        SubscriptionName = w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.Name,
                        CallbackUrl = w.webhookDelivery.WebhookSubscriptionEvent.webhookSubscription.CallbackUrl,
                        IsFailure = !w.HttpResponseCode.StartsWith("2"),
                        Status = w.webhookDelivery.DeliveryStatus
                    })
                    .GroupBy(w => new { w.SubscriptionName, w.CallbackUrl, w.Status })
                    .Select(g => new TopUserFailingSubscriptionDto
                    {
                        SubscriptionName = g.Key.SubscriptionName,
                        CallBackUrl = g.Key.CallbackUrl,
                        DeadLetterCount = g.Count(x => x.Status == Core.Constants.WebhookDeliveryStatus.DeadLetter),
                        FailedDeliveryAtttempts = g.Count(x => x.IsFailure)
                    })
                    .OrderByDescending(s => s.FailedDeliveryAtttempts)
                    .Take(5)
                    .ToListAsync(ct);

            _logger.Information("User top failing subscription fetched successfully - {0}, {1}", _authenticatedUserDetails.emailAddress, topFailingSubscriptions);

            return GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>.Success(topFailingSubscriptions, "User top failing sunscriptions fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting user top failing subscription dashboard - {0}", _authenticatedUserDetails.emailAddress);
            return GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>.Failure(null, "", HttpStatusCode.InternalServerError);

        }

    }
}
