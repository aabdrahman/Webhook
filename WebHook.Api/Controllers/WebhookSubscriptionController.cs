using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class WebhookSubscriptionController : ControllerBase
{
    private readonly IWebhookSubscriptionService _webhookSubscriptionService;
    public WebhookSubscriptionController(IWebhookSubscriptionService webhookSubscriptionService)
    {
        _webhookSubscriptionService = webhookSubscriptionService;
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    private Serilog.ILogger _logger = Log.ForContext(_className, nameof(WebhookSubscriptionController));

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        _logger = Log.ForContext(_methodName, nameof(GetAll));
        try
        {
            var result = await _webhookSubscriptionService.GetAllWebhookSubscriptionAsync(new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    [HttpGet("{webhookSubscriptionId:guid}")]
    public async Task<IActionResult> GetById(Guid webhookSubscriptionId)
    {
        _logger = Log.ForContext(_methodName, nameof(GetById));
        try
        {
            var result = await _webhookSubscriptionService.GetWebhookSubscriptionByIdAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWebhookSubscriptionDto createWebhookSubscription)
    {
        try
        {
            var result = await _webhookSubscriptionService.CreateWebhookSubscriptionAsync(createWebhookSubscription, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    [HttpPut("{webhookSubscriptionId:guid}")]
    public async Task<IActionResult> ActivateSubscription(Guid webhookSubscriptionId)
    {
        try
        {
            var result = await _webhookSubscriptionService.ActivateWebhookSubscriptionAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }

    [HttpDelete("{webhookSubscriptionId:guid}")]
    public async Task<IActionResult> Delete(Guid webhookSubscriptionId)
    {
        try
        {
            var result = await _webhookSubscriptionService.DeleteWebhookSubscriptionAsync(webhookSubscriptionId, new CancellationToken());

            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred calling endpoint.");
            GenericResponse<string> resp = GenericResponse<string>.Failure(null, "An error occrred performing operation.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
            return StatusCode((int)HttpStatusCode.InternalServerError, resp);
        }
    }
}
