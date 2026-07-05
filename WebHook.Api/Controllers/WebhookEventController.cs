using Microsoft.AspNetCore.Mvc;
using Serilog;
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
    /// </list>
    /// </returns>
    [HttpGet("{correlationId:guid}")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
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
    /// </list>
    /// </returns>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
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
    /// Creates a new webhook event raised by an internal business service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Route: <c>POST api/webhookevent</c>
    /// </para>
    /// <para>
    /// The request body must be a valid JSON object matching
    /// <see cref="CreateWebhookEventDto"/>. The service performs three
    /// sequential validation steps before persisting:
    /// </para>
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <strong>Correlation ID uniqueness</strong> — the combination of
    ///       <c>CorrelationId</c> and <c>EventType</c> must not already exist.
    ///       Returns <c>409 Conflict</c> if a duplicate is found.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Event type validation</strong> — the event type must exist
    ///       in the <c>EventCatalog</c>. Returns <c>400 Bad Request</c> for
    ///       unknown types.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <strong>Payload validation</strong> — the JSON payload is validated
    ///       against the catalog's declared fields using a dynamically constructed
    ///       CLR type. Returns <c>400 Bad Request</c> if the payload is malformed
    ///       or missing required fields, with each missing field named in the
    ///       response message.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// On success the response data field contains the newly created event's ID
    /// as a string, which can be used to correlate delivery records.
    /// </para>
    /// </remarks>
    /// <param name="createWebhookEventRequest">
    /// The event creation request, bound from the JSON request body. Must
    /// include the event type, JSON payload, and source service identifier.
    /// The correlation ID is optional but recommended for traceability.
    /// </param>
    /// <returns>
    /// An <see cref="IActionResult"/> wrapping a
    /// <see cref="GenericResponse{T}"/> of <see cref="string"/>:
    /// <list type="bullet">
    ///   <item><description><c>201 Created</c> — event persisted; data contains the new event ID.</description></item>
    ///   <item><description><c>409 Conflict</c> — duplicate CorrelationId + EventType combination.</description></item>
    ///   <item><description><c>400 Bad Request</c> — unknown event type, malformed payload, or missing required fields.</description></item>
    ///   <item><description><c>500 Internal Server Error</c> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    [HttpPost]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> CreateEvent([FromBody] CreateWebhookEventDto createWebhookEventRequest)
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
