using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

[Route("api/WebhookSubscription/{subscriptionId:guid}/verifications")]
[ApiController]
public class WebhookSubscriptionVerificationController : ControllerBase
{
    private readonly ISubscriptionVerificationService _subscriptionVerificationService;

    public WebhookSubscriptionVerificationController(ISubscriptionVerificationService subscriptionVerificationService)
    {
        _subscriptionVerificationService = subscriptionVerificationService;
    }

    private Serilog.ILogger _logger = Log.ForContext("ClassName", nameof(WebhookSubscriptionVerificationController));
    private const string _methodName = "MethodName";

    /// <summary>
    /// 
    /// </summary>
    /// <param name="subscriptionId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpGet]
    [Authorize]
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
    /// 
    /// </summary>
    /// <param name="subscriptionId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    [HttpPut("verify")]
    [Authorize(Roles = "USER")]
    public async Task<IActionResult> VerifySubscription(Guid subscriptionId, CancellationToken ct = default)
    {
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
