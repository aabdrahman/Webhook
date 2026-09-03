using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing the webhook delivery dead letter queue items.
/// </summary>
/// <remarks>
/// These endpoints allow clients to:
/// <list type="bullet">
/// <item><description>Retrieve all dead letter queue items for a delivery.</description></item>
/// <item><description>Request manaul retry of a delivery that has been moved to dead letter to enable system begin processing.</description></item>
/// </list>
/// </remarks>
[Route("api/WebhookDelivery/{deliveryId:guid}/deadLetters")]
[ApiController]
public class WebhookDeadLetterQueueController : ControllerBase
{
    private readonly IDeadLetterQueueService _deadLetterQueueService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookDeadLetterQueueController"/> class.
    /// </summary>
    /// <param name="deadLetterQueueService">
    /// The interface of the service responsible for handling dead-letter queue operations,
    /// including requests to manually retry dead-lettered webhook deliveries.
    /// </param>
    public WebhookDeadLetterQueueController(IDeadLetterQueueService deadLetterQueueService)
    {
        _deadLetterQueueService = deadLetterQueueService;
        _logger = Log.ForContext(_className, nameof(WebhookDeadLetterQueueController));
    }

    private Serilog.ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    /// <summary>
    /// Retrieves all dead letter items created for a delivery.
    /// </summary>
    /// <param name="deliveryId">
    /// <paramref name="token"/>
    /// The unique identifier of the webhook deivery id.
    /// </param>
    /// <returns>
    /// A list of delivery dead letter items.
    /// </returns>
    /// <response code="200">Dead Letter queue items were retrieved successfully.</response>
    /// <response code="400">The delivery does not have any dead letter queue item.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> GetDeadLetterQueue(Guid deliveryId, CancellationToken token = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeadLetterQueue));
        try
        {
            var result = await _deadLetterQueueService.GetDeliveryDeadKetterAsync(deliveryId, token);

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
    /// Requests a manual retry for a webhook delivery that has been moved
    /// to the dead-letter state.
    /// </summary>
    /// <param name="requestManualRetry">
    /// Contains the delivery information and justification required to
    /// request the manual retry.
    /// </param>
    /// <param name="token">
    /// A cancellation token used to cancel the request if required.
    /// </param>
    /// <returns>
    /// Returns the result of the manual retry request with the corresponding
    /// HTTP status code. If an unexpected error occurs while processing the
    /// request, an HTTP 500 Internal Server Error response is returned.
    /// </returns>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpPost]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> RequestManualRetry([FromBody] RequestManualRetryDto requestManualRetry, CancellationToken token = default)
    {
        try
        {
            var result = await _deadLetterQueueService.RequestManualRetryAsync(requestManualRetry, token);

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
