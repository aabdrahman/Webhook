using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

[Route("api/WebhookSubscription/{subscriptionId:guid}/events")]
[ApiController]
public class WebhookSubscriptionEventController : ControllerBase
{
    private readonly IWebhookSubscriptionEventService _webhookSubscriptionEventService;

    public WebhookSubscriptionEventController(IWebhookSubscriptionEventService webhookSubscriptionEventService)
    {
        _webhookSubscriptionEventService = webhookSubscriptionEventService;
    }


    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookSubscriptionEventController));

    [HttpGet]
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

    [HttpPut]
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

    [HttpDelete]
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

