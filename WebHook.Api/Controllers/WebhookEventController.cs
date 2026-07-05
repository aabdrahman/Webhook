using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class WebhookEventController : ControllerBase
{
    private readonly IWebhookEventService _webhookEventService;
    public WebhookEventController(IWebhookEventService webhookEventService)
    {
        _webhookEventService = webhookEventService;
        _logger = Log.ForContext(_className, nameof(WebhookEventController));
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger;

    [HttpGet("{correlationId:guid}")]
    public async Task<IActionResult> GetEventByCorrelationId(Guid correlationId)
    {
        _logger = Log.ForContext(_methodName, nameof(GetEventByCorrelationId));
        try
        {
            var result = await _webhookEventService.GetWebhookEventAsync(correlationId, new CancellationToken());
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null, "An error occurred invoking endpoint.", System.Net.HttpStatusCode.InternalServerError,
                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" }));

        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllEvents([FromQuery] GetWebhookEventParameters getWebhookEventParameters)
    {
        _logger = Log.ForContext(_methodName, nameof(GetAllEvents));
        try
        {
            var result = await _webhookEventService.GetWebhookEventsAsync(getWebhookEventParameters, new CancellationToken());
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null, "An error occurred invoking endpoint.", System.Net.HttpStatusCode.InternalServerError,
                                    new ErrorDetail { ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "" }));
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateEvent([FromBody] CreateWebhookEventDto createWebhookEventRequest)
    {
        _logger = Log.ForContext(_methodName, nameof(CreateEvent));
        try
        {
            var result = await _webhookEventService.CreateEventAsync(createWebhookEventRequest, new CancellationToken());
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
