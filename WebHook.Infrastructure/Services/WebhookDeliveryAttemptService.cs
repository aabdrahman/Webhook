using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class WebhookDeliveryAttemptService : IIWebhookDeliveryAttemptService
{
    private readonly RepositoryContext _repositoryContext;

    public WebhookDeliveryAttemptService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClasName", nameof(WebhookDeliveryAttemptService));

    public async Task<GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>> GetDeliveryAttemptsAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryAttemptsAsync));
        try
        {
            _logger.Information("Fetching delivery attempts for - {0}", deliveryId);

            List<WebhookDeliveryAttemptDto> deliveryAttempts = await _repositoryContext.WebhookDeliveryAttempts.Where(x => x.WebhookDeliveryId == deliveryId).Select(WebhookDeliveryAttemptMapper.ToDtoExpression()).ToListAsync(ct);

            if(!deliveryAttempts.Any())
            {
                _logger.Warning("No delivery attempts yet for delivery - {0}", deliveryId);
                return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Failure(null, "Webhook delivery attempts not yEt made for the delievry.", HttpStatusCode.NotFound);
            }

            _logger.Information("");
            return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Success(deliveryAttempts, "Webhook delivery attempts fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching delivery attempts for delievry - {0}", deliveryId);
            return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Failure(null, "An error occurrd while fetching delivery attempts.", HttpStatusCode.InternalServerError);
        }
    }
}