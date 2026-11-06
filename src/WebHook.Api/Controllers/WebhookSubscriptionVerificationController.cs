using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Verifies a subscription's callback URL. See the README section "Verifying your endpoint" for the contract
/// the subscriber's <c>/challenge</c> endpoint must follow.
/// </summary>
[Route("api/WebhookSubscription/{subscriptionId:guid}/verifications")]
[ApiController]
public class WebhookSubscriptionVerificationController : ControllerBase
{
    private readonly ISubscriptionVerificationService _subscriptionVerificationService;

    /// <summary>
    /// Initialises a new instance of the <see cref="WebhookSubscriptionVerificationController"/> class.
    /// </summary>
    /// <param name="subscriptionVerificationService">The verification service.</param>
    public WebhookSubscriptionVerificationController(ISubscriptionVerificationService subscriptionVerificationService)
    {
        _subscriptionVerificationService = subscriptionVerificationService;
    }

    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(WebhookSubscriptionVerificationController));
    private const string _methodName = "MethodName";

    /// <summary>
    /// Lists the verification attempts for a subscription, newest first.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>The attempts with their status and timestamps.</returns>
    /// <response code="200">The attempts were returned.</response>
    /// <response code="404">The subscription does not exist or belongs to another user.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpGet]
    [Authorize]
    [EnableRateLimiting("per-user-rating")]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSubscriptionVerifications(Guid subscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetSubscriptionVerifications));
        try
        {
            var result = await _subscriptionVerificationService.GetSubscriptionVerifications(subscriptionId, ct);

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
    /// Starts verification of the subscription's callback URL. The platform posts a signed challenge to
    /// <c>{callbackUrl}/challenge</c> in the background; on a correct answer the subscription becomes active.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="ct">A token to cancel the request.</param>
    /// <returns>Whether the verification was started.</returns>
    /// <response code="200">The verification was queued.</response>
    /// <response code="404">The subscription does not exist or belongs to another user.</response>
    /// <response code="409">The subscription is already active, or a verification is already in progress.</response>
    /// <response code="503">The verification could not be queued. Try again later.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPut("verify")]
    [Authorize(Roles = "User,USER")]
    [EnableRateLimiting("per-user-rating")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> VerifySubscription(Guid subscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(VerifySubscription));
        try
        {
            var result = await _subscriptionVerificationService.VerifySubscriptionCallbackUrl(subscriptionId, ct);

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
