using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing internal service clients that are authorised
/// to publish webhook events to WebhookHub.
/// </summary>
/// <remarks>
/// Service clients represent internal business applications — such as an order
/// management service or a payment gateway — that are onboarded by an Admin and
/// granted permission to publish specific event types.
///
/// Each client is issued a <c>ClientId</c> and a <c>ClientKey</c> at onboarding.
/// The <c>ClientKey</c> is shown once and never stored in plaintext — it must be
/// stored securely by the consuming service. Clients present these credentials via
/// the <c>X-Client-Id</c> and <c>X-Client-Key</c> headers on every publish request.
///
/// All endpoints on this controller require the <c>Admin</c> role.
/// </remarks>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Admin,ADMIN")]
public class WebhookServiceClientsController : ControllerBase
{
    private readonly IWebhookServiceClientService _webhookServiceClientService;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebhookServiceClientsController"/> class.
    /// </summary>
    /// <param name="webhookServiceClientService">
    /// The service responsible for service client management operations including
    /// onboarding, deactivation, reactivation, and key rotation.
    /// </param>
    public WebhookServiceClientsController(IWebhookServiceClientService webhookServiceClientService)
    {
        _webhookServiceClientService = webhookServiceClientService;
    }

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookServiceClientsController));

    /// <summary>
    /// Retrieves all onboarded service clients.
    /// </summary>
    /// <remarks>
    /// Returns a list of all registered service clients. By default only active
    /// clients are returned. Set <paramref name="includeDeactivated"/> to <c>true</c>
    /// to include deactivated clients in the response.
    /// </remarks>
    /// <param name="includeDeactivated">
    /// When <c>true</c>, deactivated service clients are included in the response
    /// alongside active ones. Defaults to <c>false</c>.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A list of <see cref="WebhookServiceClientDto"/> representing all registered
    /// service clients matching the filter.
    /// </returns>
    /// <response code="200">Service clients retrieved successfully.</response>
    /// <response code="404">No service clients were found.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>), statusCode: StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>), statusCode: StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>), statusCode: StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAllOnboardedClients(bool includeDeactivated = false, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetAllOnboardedClients));
        try
        {
            var result = await _webhookServiceClientService.GetAllClientsAsync(includeDeactivated, ct);

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
    /// Retrieves a service client by its unique client identifier.
    /// </summary>
    /// <param name="clientid">
    /// The unique client identifier assigned at onboarding — for example
    /// <c>order-service-prod</c>. This is the same value the service presents
    /// in the <c>X-Client-Id</c> header when publishing events.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="WebhookServiceClientDto"/> containing the service client details,
    /// or a descriptive error response if no client was found for the provided identifier.
    /// </returns>
    /// <response code="200">Service client retrieved successfully.</response>
    /// <response code="404">No service client was found for the provided client identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet("{clientid}")]
    [ProducesResponseType(typeof(GenericResponse<WebhookServiceClientDto>), statusCode: StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<WebhookServiceClientDto>), statusCode: StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<WebhookServiceClientDto>), statusCode: StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByClientId(string clientid, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetByClientId));
        try
        {
            var result = await _webhookServiceClientService.GetByClientIdAsync(clientid, ct);

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
    /// Deactivates an onboarded service client, preventing it from publishing
    /// further webhook events.
    /// </summary>
    /// <remarks>
    /// Deactivation is a soft operation — the client record is retained for audit
    /// purposes but the client is marked inactive. Any subsequent publish requests
    /// presenting the deactivated client's credentials will be rejected with
    /// <c>401 Unauthorized</c>.
    ///
    /// A deactivated client can be restored at any time via
    /// <c>PUT /api/WebhookServiceClients/reactivate/{clientid}</c>.
    ///
    /// If the client's credentials are believed to be compromised, deactivate the
    /// client and onboard a new one — the compromised <c>ClientKey</c> cannot be
    /// reused after deactivation.
    /// </remarks>
    /// <param name="clientid">
    /// The unique client identifier of the service client to deactivate.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming deactivation, or a descriptive error response
    /// if the client was not found or is already inactive.
    /// </returns>
    /// <response code="204">Service client deactivated successfully.</response>
    /// <response code="403">The logged in user could not be validated.</response>
    /// <response code="404">No service client was found for the provided client identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpDelete("deactivate/{clientid}")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DeactivateOnboardedClient(string clientid, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(DeactivateOnboardedClient));
        try
        {
            var result = await _webhookServiceClientService.DeactivateClientAsync(clientid, ct);

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
    /// Reactivates a previously deactivated service client, restoring its ability
    /// to publish webhook events.
    /// </summary>
    /// <remarks>
    /// Reactivation restores the client's active status. The client's existing
    /// <c>ClientId</c> and <c>ClientKey</c> remain valid after reactivation — the
    /// consuming service does not need to update its credentials unless a key
    /// rotation was performed separately.
    ///
    /// This operation is only valid for clients that are currently inactive.
    /// Attempting to reactivate an already active client returns a
    /// <c>409 Conflict</c> response.
    /// </remarks>
    /// <param name="clientid">
    /// The unique client identifier of the service client to reactivate.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming reactivation, or a descriptive error response
    /// if the client was not found or is already active.
    /// </returns>
    /// <response code="200">Service client reactivated successfully.</response>
    /// <response code="409">The service client is already active.</response>
    /// <response code="404">No service client was found for the provided client identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("reactivate/{clientid}")]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), statusCode: StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ReactivateOnboardedClient(string clientid, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ReactivateOnboardedClient));
        try
        {
            var result = await _webhookServiceClientService.ReactivateClientAsync(clientid, ct);

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
    /// Onboards a new internal service client and issues its authentication credentials.
    /// </summary>
    /// <remarks>
    /// Creates a new service client record and generates a unique <c>ClientKey</c>.
    /// The raw <c>ClientKey</c> is returned once in the response and is never stored
    /// in plaintext — it must be stored securely by the consuming service immediately.
    /// If the key is lost, a new one must be requested via
    /// <c>PUT /api/WebhookServiceClients/request-new-key</c>.
    ///
    /// The <c>ClientId</c> is provided by the Admin and must:
    /// <list type="bullet">
    ///   <item><description>Be unique across all service clients.</description></item>
    ///   <item><description>Be lowercase alphanumeric with hyphens only.</description></item>
    ///   <item><description>Follow the convention <c>{service-name}-{environment}</c> — for example <c>order-service-prod</c>.</description></item>
    /// </list>
    ///
    /// The <c>AllowedEventTypes</c> field defines which event catalog entries this
    /// client is permitted to publish. Attempts to publish an event type not in this
    /// list will be rejected with <c>403 Forbidden</c>.
    ///
    /// Sample request:
    ///
    ///     POST /api/WebhookServiceClients
    ///     {
    ///         "serviceName":      "Order Management Service",
    ///         "clientId":         "order-service-prod",
    ///         "contactEmail":     "platform@company.com",
    ///         "allowedEventTypes": ["OrderCreated", "OrderCancelled"]
    ///     }
    ///
    /// </remarks>
    /// <param name="createServiceClient">
    /// The onboarding details including the service name, client identifier,
    /// contact email, and the list of event types the client is permitted to publish.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="ServiceClientOnboardingResponse"/> containing the <c>ClientId</c>
    /// and the raw <c>ClientKey</c> — store the key securely, it will not be shown again.
    /// </returns>
    /// <response code="201">Service client onboarded successfully. ClientKey is included in the response — store it securely.</response>
    /// <response code="400">The system could not generate a valid key, or the request payload is invalid.</response>
    /// <response code="403">The requesting user does not have permission to onboard service clients.</response>
    /// <response code="404">One or more provided event types do not exist in the event catalog.</response>
    /// <response code="409">A service client with the provided ClientId already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status201Created)]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status409Conflict)]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status404NotFound)]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status403Forbidden)]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status400BadRequest)]
    [ProducesResponseType<GenericResponse<ServiceClientOnboardingResponse>>(statusCode: StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> OnboardClient([FromBody] CreateServiceClientDto createServiceClient, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(OnboardClient));
        try
        {
            var result = await _webhookServiceClientService.OnboardNewServiceClientAsync(createServiceClient, ct);

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
    /// Requests a new client key for an existing service client.
    /// </summary>
    /// <remarks>
    /// Generates and returns a new <c>ClientKey</c> for the specified service client,
    /// invalidating the previous key immediately. Use this endpoint when:
    /// <list type="bullet">
    ///   <item><description>The existing key is believed to be compromised.</description></item>
    ///   <item><description>The key was lost and needs to be reissued.</description></item>
    ///   <item><description>Routine key rotation is required by your security policy.</description></item>
    /// </list>
    ///
    /// The consuming service must update its stored <c>ClientKey</c> immediately
    /// after this operation — any publish requests using the old key will be rejected
    /// with <c>401 Unauthorized</c> once the new key is issued.
    ///
    /// The new raw <c>ClientKey</c> is returned once in the response and is never
    /// stored in plaintext. If the new key is also lost, call this endpoint again.
    /// </remarks>
    /// <param name="requestNewClientKey">
    /// Contains the <c>ClientId</c> of the service client requiring a new key,
    /// and the justification for the key rotation stored for audit purposes.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A response containing the new raw <c>ClientKey</c> — store it securely,
    /// it will not be shown again.
    /// </returns>
    /// <response code="200">New client key issued successfully. Update the consuming service immediately.</response>
    /// <response code="404">No service client was found for the provided client identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("request-new-key")]
    public async Task<IActionResult> RequestNewClientKey([FromBody] RequestNewClientKeyDto requestNewClientKey, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RequestNewClientKey));
        try
        {
            var result = await _webhookServiceClientService.RequestNewClientKeyAsync(requestNewClientKey, ct);

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
