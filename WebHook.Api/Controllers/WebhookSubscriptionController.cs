using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// API controller responsible for managing webhook subscriptions.
/// Provides endpoints for creating, retrieving, and activating/deactivating
/// webhook subscription entries that define subscribed webhooks event.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class WebhookSubscriptionController : ControllerBase
{
    private readonly IWebhookSubscriptionService _webhookSubscriptionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSubscriptionController"/> class.
    /// </summary>
    /// <param name="webhookSubscriptionService">
    /// The interface of the service responsible for handling webhook subscription operations.
    /// </param>
    public WebhookSubscriptionController(IWebhookSubscriptionService webhookSubscriptionService)
    {
        _webhookSubscriptionService = webhookSubscriptionService;
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookSubscriptionController));


    /// <summary>
    /// Retrieves all webhook subscriptions
    /// </summary>
    /// <remarks>
    /// This endpoint returns a list of all subscribed webhooks.
    /// It is to be used by admins to discover all available webhook subscriptions.
    /// </remarks>
    /// <response code="200">Successfully retrieved the list of webhook subscriptions.</response>
    /// <response code="400">Bad request. The request was invalid or malformed.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to access this resource.</response>
    /// <response code="404">No webhook subscriptions were found.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<WebhookSubscriptionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAll()
    {
        _logger = Log.ForContext(_methodName, nameof(GetAll));
        try
        {
            var result = await _webhookSubscriptionService.GetAllWebhookSubscriptionAsync(new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occurred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    /// <summary>
    /// Retrieves a webhook subsription by its unique identifier.
    /// </summary>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the webhook subscription with its key details.</param>
    /// <remarks>
    /// This endpoint returns details of a specific webhook subsription.
    /// Supply a valid Webhook Subscription Id in the route parameter.
    /// </remarks>
    /// <response code="200">Successfully retrieved the webhook subsription.</response>
    /// <response code="400">The supplied webhook subsription Id is invalid.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to access this resource.</response>
    /// <response code="404">No webhook subscription was found for the supplied Id.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<WebhookSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<WebhookSubscriptionDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<WebhookSubscriptionDto>), StatusCodes.Status500InternalServerError)]
    [HttpGet("{webhookSubscriptionId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetById(Guid webhookSubscriptionId)
    {
        _logger = Log.ForContext(_methodName, nameof(GetById));
        try
        {
            var result = await _webhookSubscriptionService.GetWebhookSubscriptionByIdAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    /// <summary>
    /// Creates a new webhook subscriptions.
    /// </summary>
    /// <param name="createWebhookSubscription">
    /// The details of the webhook subscription to create.
    /// </param>
    /// <remarks>
    /// This endpoint creates a new webhook subscription.
    ///
    /// Sample request:
    ///
    ///     POST /api/WebhookEventCatalog
    ///     {
    ///         "SubscriberName": "User 1",
    ///         "SubscribedFields": ["name", "email"],
    ///         "SubscribedEvents": ["profileCreated", "orderReceived"],
    ///         "CallBackUrl": "https://example.com/"
    ///     }
    ///
    /// </remarks>
    /// <response code="201">The webhook subscription was successfully created.</response>
    /// <response code="400">The request payload is invalid or failed validation.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to perform this action.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Create([FromBody] CreateWebhookSubscriptionDto createWebhookSubscription)
    {
        _logger = Log.ForContext(_methodName, nameof(Create));
        try
        {
            var result = await _webhookSubscriptionService.CreateWebhookSubscriptionAsync(createWebhookSubscription, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    /// <summary>
    /// The endpoint is used to activate an already deleted webhook subscription
    /// </summary>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <remarks>
    /// This endpoint allows administrators to change the activation status of a webhook from deleted to undeleted.
    /// event catalog entry.
    /// </remarks>
    /// <response code="200">The activation status was successfully updated.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to perform this action.</response>
    /// <response code="404">The specified webhook subscription was not found.</response>
    /// <response code="409">The requested activation state is already applied.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpPut("{webhookSubscriptionId:guid}")]
    [Authorize]
    public async Task<IActionResult> ActivateSubscription(Guid webhookSubscriptionId)
    {
        _logger = Log.ForContext(_methodName, nameof(ActivateSubscription));
        try
        {
            var result = await _webhookSubscriptionService.ActivateWebhookSubscriptionAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    /// <summary>
    /// The endpoint is used to deactivate a subscription.
    /// </summary>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <remarks>
    /// The endpoint allows teh subscriber or administrators to deactivate a subscription to prevent the system 
    /// from processing the callback whenever subscribed event(s) is raised.
    /// </remarks>
    /// <response code="200">The deactivation status was successfully updated.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">Unauthorized. Authentication is required.</response>
    /// <response code="403">Forbidden. You do not have permission to perform this action.</response>
    /// <response code="404">The specified webhook subscription was not found.</response>
    /// <response code="409">The requested activation state is already applied.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [Produces("application/json")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpDelete("{webhookSubscriptionId:guid}")]
    [Authorize]
    public async Task<IActionResult> Delete(Guid webhookSubscriptionId)
    {
        _logger = Log.ForContext(_methodName, nameof(Delete));
        try
        {
            var result = await _webhookSubscriptionService.DeleteWebhookSubscriptionAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    /// <summary>
    /// Retrieves all webhook subscriptions belonging to the currently authenticated user.
    /// </summary>
    /// <remarks>
    /// Returns a list of all webhook subscriptions associated with the authenticated user's
    /// account. Each subscription includes its configuration details, subscribed events,
    /// callback URL, and current active status.
    ///
    /// Only subscriptions owned by the requesting user are returned — this endpoint
    /// does not expose subscriptions belonging to other users.
    /// </remarks>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A list of <see cref="WebhookSubscriptionDto"/> representing the user's webhook
    /// subscriptions, or a descriptive error response if none are found or an error occurs.
    /// </returns>
    /// <response code="200">Subscriptions retrieved successfully.</response>
    /// <response code="401">The request does not include a valid authentication token.</response>
    /// <response code="403">The authenticated user does not have permission to access this resource.</response>
    /// <response code="404">No webhook subscriptions were found for the authenticated user.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [HttpGet("get-user-subscriptions")]
    [Authorize]
    public async Task<IActionResult> GetUserSubscriptions(CancellationToken ct)
    {
        _logger = Log.ForContext(_methodName, nameof(GetUserSubscriptions));
        try
        {
            var result = await _webhookSubscriptionService.GetUserSubscriptionsAsync(ct);

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }
}
