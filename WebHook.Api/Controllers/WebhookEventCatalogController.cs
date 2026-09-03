using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// API controller responsible for managing webhook event catalogs.
/// Provides endpoints for creating, retrieving, updating, and activating/deactivating
/// event catalog entries that define subscribable webhook event types.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class WebhookEventCatalogController : ControllerBase
{
    private readonly IWebhookEventCatalogService _webhookEventCatalogService;
    private Serilog.ILogger _logger;

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookEventCatalogController"/> class.
    /// </summary>
    /// <param name="webhookEventCatalogService">
    /// The interface of the service responsible for handling webhook event catalog operations.
    /// </param>
    public WebhookEventCatalogController(IWebhookEventCatalogService webhookEventCatalogService)
    {
        _webhookEventCatalogService = webhookEventCatalogService;
        _logger = Log.ForContext(_className, nameof(WebhookEventCatalogController));
    }

    /// <summary>
    /// Retrieves all webhook event catalog entries.
    /// </summary>
    /// <remarks>
    /// This endpoint returns a list of all configured webhook event catalog definitions.
    /// It can be used by clients to discover available webhook events.
    /// </remarks>
    /// <response code="200">Successfully retrieved the list of event catalog items.</response>
    /// <response code="400">Bad request. The request was invalid or malformed.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to access this resource.</response>
    /// <response code="404">No event catalog entries were found.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<EventCatalogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAllEventCatalog()
    {
        _logger = _logger.ForContext(_methodName, nameof(GetAllEventCatalog));
        try
        {
            var result = await _webhookEventCatalogService.GetAllEventCatalogAsync(new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            return StatusCode(500, "An error occurred.");
        }
    }

    /// <summary>
    /// Retrieves a webhook event catalog entry by its unique identifier.
    /// </summary>
    /// <param name="EventCatalogId">
    /// The unique identifier of the webhook event catalog entry.
    /// </param>
    /// <remarks>
    /// This endpoint returns details of a specific webhook event catalog entry.
    /// Supply a valid Event Catalog Id in the route parameter.
    /// </remarks>
    /// <response code="200">Successfully retrieved the webhook event catalog entry.</response>
    /// <response code="400">The supplied Event Catalog Id is invalid.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to access this resource.</response>
    /// <response code="404">No webhook event catalog entry was found for the supplied Id.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<EventCatalogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [HttpGet("{EventCatalogId:guid}")]
    [Authorize]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> GetEventCatalogById(Guid EventCatalogId)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetEventCatalogById));
        try
        {
            var result = await _webhookEventCatalogService.GetEventCatalogByIdAsync(EventCatalogId);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                    GenericResponse<string>.Failure("Operation Failed.", "An error occurred.", HttpStatusCode.InternalServerError,
                        new ErrorDetail()
                        {
                            ErrorTitle = ex.GetType().Name,
                            ErrorMessage = ex.Message,
                            ErrorDescription = ex.InnerException?.Message ?? ""
                        }
                    )
            );
        }
    }

    /// <summary>
    /// Creates a new webhook event catalog entry.
    /// </summary>
    /// <param name="createEventCatalog">
    /// The details of the webhook event catalog entry to create.
    /// </param>
    /// <remarks>
    /// This endpoint creates a new webhook event catalog definition that can be used
    /// for webhook event registration and discovery.
    ///
    /// Sample request:
    ///
    ///     POST /api/WebhookEventCatalog
    ///     {
    ///         "eventName": "CustomerCreated",
    ///         "description": "Triggered when a customer is created",
    ///         "AvailableFields": ["email", "name"]
    ///     }
    ///
    /// </remarks>
    /// <response code="201">The webhook event catalog entry was successfully created.</response>
    /// <response code="400">The request payload is invalid or failed validation.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to perform this action.</response>
    /// <response code="409">A webhook event catalog entry with the same details already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> CreateEventCatalog([FromBody] CreateEventCatalogDto createEventCatalog)
    {
        _logger = _logger.ForContext(_methodName, nameof(CreateEventCatalog));
        try
        {
            var result = await _webhookEventCatalogService.CreateNewEventCatalogAsync(createEventCatalog);

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                    GenericResponse<string>.Failure("Operation Failed.", "An error occurred.", HttpStatusCode.InternalServerError,
                        new ErrorDetail()
                        {
                            ErrorTitle = ex.GetType().Name,
                            ErrorMessage = ex.Message,
                            ErrorDescription = ex.InnerException?.Message ?? ""
                        }
                    )
            );
        }
    }

    /// <summary>
    /// Activates or deactivates a webhook event catalog entry.
    /// </summary>
    /// <param name="EventCatalogId">
    /// The unique identifier of the webhook event catalog entry.
    /// </param>
    /// <param name="isDeactivate">
    /// Indicates whether the event catalog entry should be deactivated.
    /// Set to <c>true</c> to deactivate the entry; set to <c>false</c> to activate it.
    /// </param>
    /// <remarks>
    /// This endpoint allows administrators to change the activation status of a webhook
    /// event catalog entry.
    ///
    /// Examples:
    ///
    ///     PUT /api/WebhookEventCatalog/{EventCatalogId}?isDeactivate=true
    ///
    /// Deactivates the specified event catalog entry.
    ///
    ///     PUT /api/WebhookEventCatalog/{EventCatalogId}?isDeactivate=false
    ///
    /// Activates the specified event catalog entry.
    /// </remarks>
    /// <response code="200">The activation status was successfully updated.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to perform this action.</response>
    /// <response code="404">The specified webhook event catalog entry was not found.</response>
    /// <response code="409">The requested activation state is already applied.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [HttpPut("{EventCatalogId:guid}")]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> ActivationAction(Guid EventCatalogId, bool isDeactivate = true)
    {
        _logger = _logger.ForContext(_methodName, nameof(ActivationAction));
        try
        {
            var result = await _webhookEventCatalogService.EventCatalogActivationAsync(EventCatalogId, isDeactivate);

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                    GenericResponse<string>.Failure("Operation Failed.", "An error occurred.", HttpStatusCode.InternalServerError,
                        new ErrorDetail()
                        {
                            ErrorTitle = ex.GetType().Name,
                            ErrorMessage = ex.Message,
                            ErrorDescription = ex.InnerException?.Message ?? ""
                        }
                    )
            );
        }
    }
}
