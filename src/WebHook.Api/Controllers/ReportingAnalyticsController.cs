using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.RequestParameter;

namespace WebHook.Api.Controllers;


/// <summary>
/// Handles HTTP requests for reporting and analytics data within the WebhookHub platform.
/// Exposes aggregated metrics and event summaries for dashboard consumption.
/// This is only available to users with ADMIN role.
/// </summary>
[Route("api/[controller]")]
[ApiController]

public class ReportingAnalyticsController : ControllerBase
{
    private readonly IReportingAnalyticsService _reportingAnalyticsService;
    /// <summary>
    /// 
    /// </summary>
    /// <param name="reportingAnalyticsService"></param>
    public ReportingAnalyticsController(IReportingAnalyticsService reportingAnalyticsService)
    {
        _reportingAnalyticsService = reportingAnalyticsService;
    }

    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(ReportingAnalyticsController));
    private const string _methodName = "MethodName";

    /// <summary>
    /// Retrieves the five most recently processed webhook events across all subscriptions.
    /// </summary>
    /// <remarks>
    /// Returns a summarised view of recent webhook activity, including event type, source,
    /// correlation ID, and the total number of delivery attempts per event.
    /// This endpoint is backed by the <c>vw_GetMostRecentEvents</c> database view and is
    /// intended for use on the admin analytics dashboard.
    ///
    /// <b>Response shape:</b>
    /// <code>
    /// {
    ///   "isSuccessful": true,
    ///   "data": [
    ///     {
    ///       "createdAt": "2026-10-01T18:00:00Z",
    ///       "correlationId": "abc-123",
    ///       "eventType": "CUSTOMERCREATED",
    ///       "source": "crm-service",
    ///       "deliveryCount": 3
    ///     }
    ///   ]
    /// }
    /// </code>
    /// </remarks>
    /// <param name="ct">Cancellation token passed by the ASP.NET Core request pipeline.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — successfully retrieved recent events.</description></item>
    ///   <item><description><b>4xx</b> — service-level failure with a structured error response.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — unhandled exception with error detail.</description></item>
    /// </list>
    /// </returns>
    [Authorize(Roles = "Admin, ADMIN")]
    [HttpGet("most-recent-events")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetMostRecentEventsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetMostRecentEventsAsync));
        try
        {
            var response = await _reportingAnalyticsService.GetMostRecentEventsAsync(ct);
            if (response.IsSuccessful)
            {
                return Ok(response);
            }
            else
            {
                return StatusCode((int)response.HttpStatusCode, response);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Retrieves analytics for the top active event catalogs,
    /// ranked by total events raised.
    /// </summary>
    /// <remarks>
    /// Returns up to 5 active event catalogs with their aggregated metrics:
    /// total events raised, number of active subscriptions, and the timestamp
    /// of the most recently raised event. Catalogs with no events or subscriptions
    /// are included with zero counts.
    /// </remarks>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — Analytics retrieved successfully.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — An unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [ProducesResponseType(typeof(GenericResponse<IEnumerable<EventCatalogReportingAnalyticsEntity>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "Admin, ADMIN")]
    [HttpGet("get-event-catalog-analytics")]
    public async Task<IActionResult> GetEventCatalogAnalytics(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetEventCatalogAnalytics));
        try
        {
            var response = await _reportingAnalyticsService.GetTopActiveEventCatalogsAsync(ct);
            return StatusCode((int)response.HttpStatusCode, response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Retrieves analytics for the top failing webhook subscriptions,
    /// ranked by total delivery failures.
    /// </summary>
    /// <remarks>
    /// Returns subscriptions with the highest failure counts, including
    /// aggregated metrics such as total attempts, failed deliveries, and
    /// the most recent failure timestamp. Useful for identifying unhealthy
    /// subscriptions that may require attention or remediation.
    /// </remarks>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — Top failing subscriptions retrieved successfully.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — An unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<TopFailingSubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "Admin, ADMIN")]
    [HttpGet("get-top-failing-subscriptions")]
    public async Task<IActionResult> GetTopFailingSubscriptionsAsync(CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetTopFailingSubscriptionsAsync));
        try
        {
            var response = await _reportingAnalyticsService.GetTopFailingSubscriptionsAsync(ct);
            return StatusCode((int)response.HttpStatusCode, response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Retrieves a summary of key metrics for the admin dashboard.
    /// </summary>
    /// <remarks>
    /// Returns a platform-wide snapshot including active subscriptions, event catalogs,
    /// service clients, onboarded users, pending deliveries, dead-letter queue depth,
    /// and total events raised today.
    /// </remarks>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — Dashboard summary retrieved successfully.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — An unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [ProducesResponseType(typeof(GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "Admin, ADMIN")]
    [HttpGet("get-admin-dashboard-summary")]
    public async Task<IActionResult> GetAdminDashboard(CancellationToken ct = default)
    {
        _logger.ForContext(_methodName, nameof(GetAdminDashboard));
        try
        {
            var response = await _reportingAnalyticsService.GetAdminDashboardSummaryAsync(ct);
            return StatusCode((int)response.HttpStatusCode, response);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    [Authorize(Roles = "User,USER")]
    [HttpGet("get-user-delivery-dashboard")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<UserDeliveryDashboardDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserDeliveryDashboard(CancellationToken ct = default) 
    {
        _logger = _logger.ForContext(_methodName, nameof(GetUserDeliveryDashboard));
        try
        {
            var result = await _reportingAnalyticsService.GetUserDeliveryDashboardAsync(ct);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Retrieves the top failing webhook subscriptions for the currently authenticated user,
    /// ranked by total delivery failures.
    /// </summary>
    /// <remarks>
    /// Returns the user's subscriptions with the highest failure counts, useful for
    /// identifying endpoints that are consistently rejecting webhook deliveries.
    /// Only subscriptions belonging to the authenticated user are included.
    /// </remarks>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — Top failing subscriptions retrieved successfully.</description></item>
    ///   <item><description><b>400 Bad Request</b> — The request could not be processed.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — An unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [Authorize(Roles = "User,USER")]
    [HttpGet("get-user-top-failing-subscription")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetUserTopFailingSubscriptionDashboard(CancellationToken ct = default)
    {
        try
        {
            var result = await _reportingAnalyticsService.GetUserTopFailingSubscriptionDashboardAsync(ct);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Retrieves a summary of key delivery metrics for the currently authenticated user's dashboard.
    /// </summary>
    /// <remarks>
    /// Returns aggregated statistics scoped to the authenticated user's subscriptions,
    /// including active subscriptions, subscribed event catalogs, pending deliveries,
    /// dead-letter queue depth, and overall delivery success rate.
    /// Supports optional date range filtering via <paramref name="request"/> to narrow
    /// metrics to a specific period.
    /// </remarks>
    /// <param name="request">
    /// Optional query parameters for filtering the summary by date range
    /// (<c>StartDate</c> and <c>EndDate</c>). Defaults to the current day when omitted.
    /// </param>
    /// <param name="ct">Cancellation token for the async operation.</param>
    /// <returns>
    /// <list type="bullet">
    ///   <item><description><b>200 OK</b> — Dashboard summary retrieved successfully.</description></item>
    ///   <item><description><b>400 Bad Request</b> — Invalid request parameters.</description></item>
    ///   <item><description><b>500 Internal Server Error</b> — An unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [ProducesResponseType(typeof(GenericResponse<UserDashboardSummaryReportAnalyticsEntity>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<UserDashboardSummaryReportAnalyticsEntity>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [Authorize(Roles = "User,USER")]
    [HttpGet("get-user-dashboard-summary")]
    public async Task<IActionResult> GetUserDashboardSummaryAsync([FromQuery] UserDashboardSummaryAnalyticsRequestParamterDto request, CancellationToken ct = default)
    {
        try
        {
            var result = await _reportingAnalyticsService.GetUserDashboardSummaryAsync(request, ct);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }
}
