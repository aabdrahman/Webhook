using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;


/// <summary>
/// Handles HTTP requests for reporting and analytics data within the WebhookHub platform.
/// Exposes aggregated metrics and event summaries for dashboard consumption.
/// This is only available to users with ADMIN role.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin, ADMIN")]
public class ReportingAnalyticsController : ControllerBase
{
    private readonly IReportingAnalyticsService _reportingAnalyticsService;
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
}
