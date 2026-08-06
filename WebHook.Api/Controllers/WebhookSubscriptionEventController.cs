using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing the webhook events subscribed to by a webhook subscription.
/// </summary>
/// <remarks>
/// These endpoints allow clients to:
/// <list type="bullet">
/// <item><description>Retrieve all events currently subscribed to by a webhook subscription.</description></item>
/// <item><description>Subscribe an existing webhook subscription to a new event.</description></item>
/// <item><description>Remove an existing event subscription.</description></item>
/// </list>
/// </remarks>
[Route("api/WebhookSubscription/{subscriptionId:guid}/events")]
[ApiController]
public class WebhookSubscriptionEventController : ControllerBase
{
    private readonly IWebhookSubscriptionEventService _webhookSubscriptionEventService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookSubscriptionEventController"/> class.
    /// </summary>
    /// <param name="webhookSubscriptionEventService">
    /// The interface of the service responsible for handling webhook subscription event operations.
    /// </param>
    public WebhookSubscriptionEventController(IWebhookSubscriptionEventService webhookSubscriptionEventService)
    {
        _webhookSubscriptionEventService = webhookSubscriptionEventService;
    }


    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookSubscriptionEventController));

    /// <summary>
    /// Retrieves all events subscribed to by a webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <returns>
    /// A list of subscribed webhook events.
    /// </returns>
    /// <response code="200">Subscribed events were retrieved successfully.</response>
    /// <response code="400">The subscription identifier is invalid.</response>
    /// <response code="404">The specified webhook subscription was not found.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSubscribedEvents(Guid subscriptionId)
    {
        _logger = Log.ForContext(_methodName, nameof(GetSubscribedEvents));
        try
        {
            var result = await _webhookSubscriptionEventService.GetSubscribedEventsAsync(subscriptionId, new CancellationToken());
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
    /// Subscribes a webhook subscription to a webhook event.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the webhook event to subscribe to.
    /// </param>
    /// <returns>
    /// A response indicating whether the subscription was successfully created.
    /// </returns>
    /// <response code="200">The webhook subscription was successfully subscribed to the event.</response>
    /// <response code="400">The request is invalid or the event name is missing.</response>
    /// <response code="404">The webhook subscription or event was not found.</response>
    /// <response code="409">The webhook subscription is already subscribed to the specified event.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SubscribeEvent(Guid subscriptionId, string eventName)
    {
        _logger = Log.ForContext(_methodName, nameof(SubscribeEvent));
        try
        {
            var result = await _webhookSubscriptionEventService.SubscribeToEventAsync(subscriptionId, eventName, new CancellationToken());
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
    /// Removes an event subscription from a webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the webhook event to unsubscribe from.
    /// </param>
    /// <returns>
    /// A response indicating whether the event was successfully removed from the subscription.
    /// </returns>
    /// <response code="200">The event was successfully removed from the subscription.</response>
    /// <response code="400">The request is invalid or the event name is missing.</response>
    /// <response code="404">The webhook subscription or event was not found.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> UnsubscribeEvent(Guid subscriptionId, string eventName)
    {
        _logger = Log.ForContext(_methodName, nameof(UnsubscribeEvent));
        try
        {
            var result = await _webhookSubscriptionEventService.UnsubscribeFromEventAsync(subscriptionId, eventName, new CancellationToken());
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

