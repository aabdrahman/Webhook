using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing the event catalog subscriptions of a registered
/// service client.
/// </summary>
/// <remarks>
/// Service clients are onboarded via <c>POST /api/WebhookServiceClients</c> and
/// assigned an initial set of event catalog entries they are permitted to publish.
/// These endpoints allow Admins to query, extend, or restrict that set after onboarding
/// without having to re-onboard the client.
///
/// All endpoints are scoped to a specific service client via the
/// <c>serviceclientid</c> route parameter.
/// </remarks>
[Route("api/WebhookServiceClients/{serviceclientid:guid}/eventcatalogs")]
[ApiController]
[Authorize(Roles = "ADMIN, Admin")]
public class WebhookServiceClientEventCatalogsController : ControllerBase
{
    private readonly IWebhookServiceClientCatalogService _webhookServiceClientCatalogService;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="WebhookServiceClientEventCatalogsController"/> class.
    /// </summary>
    /// <param name="webhookServiceClientCatalogService">
    /// The service responsible for managing event catalog subscriptions
    /// for registered service clients.
    /// </param>
    public WebhookServiceClientEventCatalogsController(IWebhookServiceClientCatalogService webhookServiceClientCatalogService)
    {
        _webhookServiceClientCatalogService = webhookServiceClientCatalogService;
    }

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookServiceClientEventCatalogsController));

    /// <summary>
    /// Retrieves all event catalog entries subscribed to by the specified service client.
    /// </summary>
    /// <remarks>
    /// Returns the list of event catalog entries the service client is currently
    /// permitted to publish. By default only active subscriptions are returned.
    /// Set <paramref name="includeDeactivated"/> to <c>true</c> to include catalog
    /// entries that have been unsubscribed or deactivated.
    /// </remarks>
    /// <param name="serviceclientid">
    /// The unique identifier of the service client whose catalog subscriptions
    /// are being retrieved.
    /// </param>
    /// <param name="includeDeactivated">
    /// When <c>true</c>, deactivated catalog subscriptions are included alongside
    /// active ones. Defaults to <c>false</c>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A list of <see cref="WebhookServiceClientCatalogDto"/> representing the
    /// event catalog subscriptions for the specified service client.
    /// </returns>
    /// <response code="200">Event catalog subscriptions retrieved successfully.</response>
    /// <response code="404">No subscriptions were found for the specified service client.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>), statusCode: StatusCodes.Status200OK, Description = "Subscribed event atalogs fetched successfully for the client id.")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>), statusCode: StatusCodes.Status404NotFound, Description = "")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>), statusCode: StatusCodes.Status500InternalServerError, Description = "Error occurred in the service and caughth successfully.")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status500InternalServerError, Description = "Error occured when invoking the endpoint.")]
    public async Task<IActionResult> GetSubscribedCatalogs(Guid serviceclientid, bool includeDeactivated = false, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetSubscribedCatalogs));
        try
        {
            var result = await _webhookServiceClientCatalogService.GetSubscribedCatalogsAsync(serviceclientid, includeDeactivated, ct);

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
    /// Removes an event catalog subscription from the specified service client.
    /// </summary>
    /// <remarks>
    /// Once unsubscribed, any attempt by the service client to publish an event
    /// of the removed catalog type will be rejected with <c>403 Forbidden</c>.
    ///
    /// The subscription record is soft-deleted — it can be restored by calling
    /// <c>POST /api/WebhookServiceClients/{serviceclientid}/eventcatalogs</c>
    /// with the same catalog name.
    /// </remarks>
    /// <param name="serviceclientid">
    /// The unique identifier of the service client to unsubscribe.
    /// </param>
    /// <param name="catalogName">
    /// The normalized name of the event catalog entry to unsubscribe from —
    /// for example <c>OrderCreated</c>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the unsubscription, or a descriptive error
    /// response if the subscription or service client was not found.
    /// </returns>
    /// <response code="200">Event catalog unsubscribed successfully.</response>
    /// <response code="404">The service client or catalog subscription was not found or is already inactive.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status200OK, Description = "The unsubscribe operation was completed successfully.")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status404NotFound, Description = "The subscription details does not exist or has been ddisbaled")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status200OK, Description = "An error occurred while unsubscribing from event catalog.")]
    public async Task<IActionResult> UnsubscribeCatalog(Guid serviceclientid, string catalogName, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(UnsubscribeCatalog));
        try
        {
            var result = await _webhookServiceClientCatalogService.UnSubscribeFromCatalogAsync(serviceclientid, catalogName, ct);

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
    /// Subscribes the specified service client to an event catalog entry,
    /// authorising it to publish that event type.
    /// </summary>
    /// <remarks>
    /// If the service client was previously subscribed to the catalog but the
    /// subscription was deactivated, this endpoint reactivates the existing
    /// subscription rather than creating a duplicate.
    ///
    /// If the subscription is already active, a <c>409 Conflict</c> response
    /// is returned.
    ///
    /// The event catalog entry must exist and be active. Providing the name of
    /// a non-existent or deactivated catalog entry returns <c>404 Not Found</c>.
    /// </remarks>
    /// <param name="serviceclientid">
    /// The unique identifier of the service client to subscribe.
    /// </param>
    /// <param name="catalogName">
    /// The normalized name of the event catalog entry to subscribe to —
    /// for example <c>OrderCreated</c>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the subscription or reactivation, or a
    /// descriptive error response if the service client or catalog entry was not found.
    /// </returns>
    /// <response code="200">Event catalog subscribed or reactivated successfully.</response>
    /// <response code="404">The service client or event catalog entry was not found or is deactivated.</response>
    /// <response code="409">The service client is already actively subscribed to the specified event catalog.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status200OK, Description = "The provided event catalog has been subscried successfully or an already deactivated has been reactivated.")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status409Conflict, Description = "The provided event catalog has already been subscribed and is currently active.")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status404NotFound, Description = "The provided service client id or event catalog name does not exist or has been deactivated.")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status500InternalServerError, Description = "An unexpected error occurred when performing operation.")]
    public async Task<IActionResult> SubscribeCatalog(Guid serviceclientid, string catalogName, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(SubscribeCatalog));
        try
        {
            var result = await _webhookServiceClientCatalogService.SubscribeToCatalogAsync(serviceclientid, catalogName, ct);

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
