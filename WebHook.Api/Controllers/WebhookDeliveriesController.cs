using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.RequestParameter;

namespace WebHook.Api.Controllers;

/// <summary>
/// The controller that is used to perfom webhook deliveries enquiries.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "ADMIN,Admin")]
public class WebhookDeliveriesController : ControllerBase
{
    private readonly IWebhookDeliveryService _webhookDeliveryService;

    /// <summary>
    /// The constructor that is needeed by the DI to create an instance of teh controller to handle http requests.
    /// </summary>
    /// <param name="webhookDeliveryService"></param>
    public WebhookDeliveriesController(IWebhookDeliveryService webhookDeliveryService)
    {
        _webhookDeliveryService = webhookDeliveryService;
    }

    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(WebhookDeliveriesController));

    /// <summary>
    /// Retrieves a paginated list of webhook deliveries based on the supplied filter and pagination parameters.
    /// </summary>
    /// <remarks>
    /// By default, deliveries created within the last three days up to the current date are returned.
    /// The result can be filtered by creation date, subscription, and delivery status.
    ///
    /// Pagination metadata is returned in the <c>X-Pagination</c> response header.
    /// </remarks>
    /// <param name="webhookDeliveryQueryRequestParameter">
    /// Query parameters used to filter and paginate the webhook deliveries:
    ///
    /// - <c>StartDate</c>: The inclusive lower boundary for the delivery <c>CreatedAt</c> date.
    ///   Defaults to the current date minus three days.
    ///
    /// - <c>EndDate</c>: The inclusive upper boundary for the delivery <c>CreatedAt</c> date.
    ///   Defaults to the current date.
    ///
    /// - <c>PageSize</c>: The maximum number of deliveries to return in the current page.
    ///
    /// - <c>PageNumber</c>: The page number to retrieve. Used to calculate the database offset.
    ///
    /// - <c>SubscriptionId</c>: Optional subscription identifier. When provided, only deliveries
    ///   associated with the specified subscription are returned.
    ///
    /// - <c>DeliveryStatus</c>: Optional delivery status filter. When provided, only deliveries
    ///   with the specified status are returned.
    /// </param>
    /// <param name="ct">
    /// Cancellation token that is triggered when the HTTP request is cancelled.
    /// </param>
    /// <returns>
    /// A paginated collection of webhook delivery records together with pagination metadata.
    /// </returns>
    /// <response code="200">
    /// The deliveries were retrieved successfully. Pagination metadata is included in the
    /// <c>X-Pagination</c> response header.
    /// </response>
    /// <response code="401">
    /// The request is unauthenticated. A valid authentication token is required.
    /// </response>
    /// <response code="403">
    /// The authenticated user does not have permission to retrieve webhook deliveries.
    /// </response>
    /// <response code="500">
    /// An unexpected error occurred while retrieving the webhook deliveries.
    /// </response>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<PagedListItem<WebhookDeliveryDto>>), StatusCodes.Status200OK, Description = "The webhook deliveries were retrieved successfully.")]
    [ProducesResponseType(typeof(GenericResponse<PagedListItem<WebhookDeliveryDto>>), StatusCodes.Status401Unauthorized, Description = "Authentication is required to access webhook deliveries.")]
    [ProducesResponseType(typeof(GenericResponse<PagedListItem<WebhookDeliveryDto>>), StatusCodes.Status403Forbidden, Description = "The authenticated user is not authorized to access webhook deliveries.")]
    [ProducesResponseType(typeof(GenericResponse<PagedListItem<WebhookDeliveryDto>>), StatusCodes.Status500InternalServerError, Description = "An unexpected error occurred while retrieving webhook deliveries.")]
    public async Task<IActionResult> GetDeliveries([FromQuery] WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequestParameter, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveries));
        try
        {
            var result = await _webhookDeliveryService.GetDeliveriesAsync(webhookDeliveryQueryRequestParameter, ct);

            if(result.IsSuccessful && result.ResponseData is not null && result.ResponseData.metaData is not null)
            {
                Response.HttpContext.Response.Headers.Append("X-Pagination", new Microsoft.Extensions.Primitives.StringValues(JsonSerializer.Serialize(result.ResponseData.metaData)));
            }

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
    /// Retrieves the details of a webhook delivery using its unique identifier.
    /// </summary>
    /// <param name="deliveryId">
    /// The unique identifier of the webhook delivery.
    /// </param>
    /// <param name="ct">
    /// Cancellation token that is triggered when the HTTP request is cancelled.
    /// </param>
    /// <returns>
    /// The details of the specified webhook delivery.
    /// </returns>
    /// <response code="200">
    /// The webhook delivery details were retrieved successfully.
    /// </response>
    /// <response code="401">
    /// Authentication is required to access the webhook delivery.
    /// </response>
    /// <response code="403">
    /// The authenticated user is not authorized to access the webhook delivery.
    /// </response>
    /// <response code="404">
    /// No webhook delivery exists with the specified identifier.
    /// </response>
    /// <response code="500">
    /// An unexpected error occurred while retrieving the webhook delivery details.
    /// </response>
    [HttpGet("{deliveryId:guid}")]
    [ProducesResponseType(typeof(GenericResponse<WebhookDeliveryDetailsDto>), StatusCodes.Status200OK, Description = "The webhook delivery details were retrieved successfully.")]
    [ProducesResponseType(typeof(GenericResponse<WebhookDeliveryDetailsDto>), StatusCodes.Status401Unauthorized, Description = "Authentication is required to access the webhook delivery.")]
    [ProducesResponseType(typeof(GenericResponse<WebhookDeliveryDetailsDto>), StatusCodes.Status403Forbidden, Description = "The authenticated user is not authorized to access the webhook delivery.")]
    [ProducesResponseType(typeof(GenericResponse<WebhookDeliveryDetailsDto>), StatusCodes.Status404NotFound, Description = "No webhook delivery exists with the specified identifier.")]
    [ProducesResponseType(typeof(GenericResponse<WebhookDeliveryDetailsDto>), StatusCodes.Status500InternalServerError, Description = "An unexpected error occurred while retrieving the webhook delivery details.")]

    public async Task<IActionResult> GetDeliveryDetails(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryDetails));
        try
        {
            var result = await _webhookDeliveryService.GetDeliveryDetailsAsync(deliveryId, ct);
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
