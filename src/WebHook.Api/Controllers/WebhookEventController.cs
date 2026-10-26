using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Exposes HTTP endpoints for creating and querying webhook events.
/// </summary>
/// <remarks>
/// <para>
/// This controller is the HTTP entry point for the webhook event lifecycle.
/// All business logic and validation is delegated to
/// <see cref="IWebhookEventService"/> — the controller is responsible only
/// for translating HTTP requests into service calls and mapping service
/// responses back to HTTP status codes.
/// </para>
/// <para>
/// Base route: <c>api/webhookevent</c>
/// </para>
/// </remarks>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
public class WebhookEventController : ControllerBase
{
    private readonly IWebhookEventService _webhookEventService;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookEventController"/>.
    /// </summary>
    /// <param name="webhookEventService">
    /// The service that handles event creation, retrieval, and filtering.
    /// Injected by the ASP.NET Core DI container.
    /// </param>
    public WebhookEventController(IWebhookEventService webhookEventService)
    {
        _webhookEventService = webhookEventService;
        _logger = Log.ForContext(_className, nameof(WebhookEventController));
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger;

    /// <summary>
    /// Retrieves all webhook events associated with the specified correlation ID.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A correlation ID groups all events raised by a single originating
    /// business transaction. For example, a customer onboarding request that
    /// raises both <c>CustomerCreated</c> and <c>AccountApproved</c> would
    /// produce two events sharing the same correlation ID. This endpoint
    /// returns all of them.
    /// </para>
    /// <para>
    /// Route: <c>GET api/webhookevent/{correlationId}</c>
    /// </para>
    /// <para>
    /// The <c>{correlationId:guid}</c> route constraint ensures ASP.NET Core
    /// rejects non-GUID values with a <c>400 Bad Request</c> before the action
    /// is even invoked.
    /// </para>
    /// </remarks>
    /// <param name="correlationId">
    /// The GUID correlation ID of the originating business transaction.
    /// </param>
    /// <returns>
    /// An <see cref="IActionResult"/> wrapping a
    /// <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>:
    /// <list type="bullet">
    ///   <item><description><c>200 OK</c> — events found and returned.</description></item>
    ///   <item><description><c>404 Not Found</c> — no events exist for the provided correlation ID.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> — an unexpected error occurred.</description></item>
    ///   <item><description><c>429 Too Many Requests</c> — Too many reqeusts within the configured rate limit</description></item>
    /// </list>
    /// </returns>
    [HttpGet("{correlationId:guid}")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> GetEventByCorrelationId(Guid correlationId)
    {
        _logger = Log.ForContext(_methodName, nameof(GetEventByCorrelationId));
        try
        {
            var result = await _webhookEventService.GetWebhookEventAsync(correlationId, new CancellationToken());
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null, "An error occurred invoking endpoint.", System.Net.HttpStatusCode.InternalServerError,
                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" }));

        }
    }

    /// <summary>
    /// Retrieves a filtered list of webhook events using the provided query
    /// parameters.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Route: <c>GET api/webhookevent</c>
    /// </para>
    /// <para>
    /// All parameters are passed as query string values via
    /// <c>[FromQuery]</c>. The date range fields
    /// (<c>CreatedAtFrom</c> and <c>CreatedAtTo</c>) are mandatory; all other
    /// fields are optional and stack as additional filters when provided.
    /// </para>
    /// <para>
    /// This endpoint always returns <c>200 OK</c> on a successful query, even
    /// when the filtered result set is empty. An empty list is a valid filtered
    /// outcome — callers should check the count of the returned data rather
    /// than the status code to determine whether results were found.
    /// </para>
    /// <para>
    /// Supported query parameters:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>createdAtFrom</c> (required) — inclusive lower bound of the creation date range.</description></item>
    ///   <item><description><c>createdAtTo</c> (required) — inclusive upper bound of the creation date range.</description></item>
    ///   <item><description><c>source</c> (optional) — exact match on the originating service name.</description></item>
    ///   <item><description><c>eventType</c> (optional) — event type name, compared case-insensitively.</description></item>
    ///   <item><description><c>status</c> (optional) — delivery status string, parsed case-insensitively. Invalid values are silently ignored.</description></item>
    ///   <item><description><c>correlationId</c> (optional) — GUID of the originating business transaction.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="getWebhookEventParameters">
    /// The query parameters bound from the request query string.
    /// </param>
    /// <returns>
    /// An <see cref="IActionResult"/> wrapping a
    /// <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookEventDto}"/>:
    /// <list type="bullet">
    ///   <item><description><c>200 OK</c> — query executed; data may be an empty list.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> — an unexpected error occurred.</description></item>
    ///   <item><description><c>429 Too Many Requests</c> — Too many requests within the configured limit.</description></item>
    /// </list>
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> GetAllEvents([FromQuery] GetWebhookEventParameters getWebhookEventParameters)
    {
        _logger = Log.ForContext(_methodName, nameof(GetAllEvents));
        try
        {
            var result = await _webhookEventService.GetWebhookEventsAsync(getWebhookEventParameters, new CancellationToken());
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null, "An error occurred invoking endpoint.", System.Net.HttpStatusCode.InternalServerError,
                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" }));
        }
    }

    /// <summary>
    /// Publishes a new webhook event to be delivered to all subscribers
    /// registered for the specified event type.
    /// </summary>
    /// <remarks>
    /// This endpoint is intended for onboarded internal services only. Every request must include valid service client credentials in the request headers — requests without credentials or with invalid credentials are rejected before the payloadis processed.
    /// The full validation flow is:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <strong>Service Client Authentication</strong> — the <c>X-Client-Id</c> and <c>X-Client-Key</c> headers must both be present and non-empty.
    ///       The <c>ClientId</c> is looked up in the service client registry and the raw <c>ClientKey</c> is validated against the stored hash.
    ///       Returns <c>401 Unauthorized</c> if either header is missing, the client is not found, or the key does not match.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Event Type Authorization</strong> — the event type provided in the request body must be in the service client's assigned event catalog.
    ///       A client that was not granted permission to publish a given event type at onboarding will receive <c>403 Forbidden</c>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Correlation ID Uniqueness</strong> — the correlation ID must be unique for the given event type. Duplicate correlation IDs are rejected with <c>409 Conflict</c> to prevent double-publishing.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Payload Schema Validation</strong> — the JSON payload is validated against the field schema declared in the event catalog entry.
    ///       Missing or unrecognised fields are rejected with <c>400 Bad Request</c> and a descriptive error identifying each failing field.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// On success the event is persisted and published to the internal delivery pipeline for fan-out to all registered subscribers.
    /// Sample request:
    ///
    ///     POST /api/webhookevent
    ///     X-Client-Id:  order-service-prod
    ///     X-Client-Key: &lt;raw client key issued at onboarding&gt;
    ///     Content-Type: application/json
    ///
    ///     {
    ///         "eventType":     "OrderCreated",
    ///         "payload":       "{ \"orderId\": \"abc-123\", \"customerId\": \"xyz-456\" }",
    ///         "source":        "order-service-prod",
    ///         "correlationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6"
    ///     }
    ///
    /// </remarks>
    /// <param name="createWebhookEventRequest">
    /// The event payload including the event type, JSON body, source identifier, and an optional correlation ID. If no correlation ID is provided one will be generated automatically.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the event was accepted into the delivery pipeline, or a descriptive error response if any validation step fails.
    /// </returns>
    /// <response code="201">Event accepted and queued for delivery to all registered subscribers.</response>
    /// <response code="400">The payload failed schema validation — response identifies each failing field.</response>
    /// <response code="401">The X-Client-Id or X-Client-Key header is missing, unknown, or invalid.</response>
    /// <response code="403">The service client is not authorised to publish the specified event type.</response>
    /// <response code="409">A duplicate correlation ID was detected for this event type.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [AllowAnonymous]
    [ServiceFilter(type: typeof(ClientValidationFilter))]
    public async Task<IActionResult> CreateEvent([FromBody] CreateWebhookEventDto createWebhookEventRequest, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(CreateEvent));
        try
        {
            var result = await _webhookEventService.CreateEventAsync(createWebhookEventRequest, new CancellationToken());
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null, "An error occurred invoking endpoint.", System.Net.HttpStatusCode.InternalServerError,
                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" }));
        }
    }
}
