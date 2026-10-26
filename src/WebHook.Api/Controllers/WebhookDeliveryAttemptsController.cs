using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.RequestParameter;

namespace WebHook.Api.Controllers;

/// <summary>
/// The controller that is used to perfom webhook delivery attempts enquiries.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class WebhookDeliveryAttemptsController : ControllerBase
{
    private readonly IWebhookDeliveryAttemptService _webhookDeliveryAttemptService;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="webhookDeliveryAttemptService"></param>
    public WebhookDeliveryAttemptsController(IWebhookDeliveryAttemptService webhookDeliveryAttemptService)
    {
        _webhookDeliveryAttemptService = webhookDeliveryAttemptService;
    }

    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryAttemptsController));

    /// <summary>
    /// The endpoint fetches delivery attempts for a particular delivery.
    /// </summary>
    /// <param name="deliveryId">
    /// The unique identifier of the delivery.
    /// </param>
    /// <param name="ct">
    /// The cancellation token that specifies if an operation has been cancelled.
    /// </param>
    /// <returns>
    /// <reponse code="200">This specifies that the operation was successful and delivery attempts are retrieved successfully.</reponse>
    /// <reponse code="404">This specifies that the operation was successful and no delivery attempts are retrieved.</reponse>
    /// <response code="401"> The request is unauthenticated. A valid authentication token is required.</response>
    /// <response code="403">The authenticated user does not have permission to retrieve webhook deliveries.</response>
    /// <response code="500">An unexpected error occurred while retrieving the webhook deliveries.</response>
    /// </returns>
    [HttpGet("{deliveryId:guid}")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>), StatusCodes.Status200OK, Description = "The webhook delivery attempts were retrieved successfully.")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>), StatusCodes.Status401Unauthorized, Description = "Authentication is required to access webhook delivery attempts.")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>), StatusCodes.Status403Forbidden, Description = "The authenticated user is not authorized to access webhook delivery attempts.")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>), StatusCodes.Status500InternalServerError, Description = "An unexpected error occurred while retrieving webhook delivery attempts.")]
    public async Task<IActionResult> GetDeliveryAttempts(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryAttempts));
        try
        {
            var result = await _webhookDeliveryAttemptService.GetDeliveryAttemptsAsync(deliveryId, ct);

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
