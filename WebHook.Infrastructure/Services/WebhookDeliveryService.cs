using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Core.RequestParameter;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public class WebhookDeliveryService : IWebhookDeliveryService
{
    private readonly RepositoryContext _repositoryContext;

    public WebhookDeliveryService(RepositoryContext repositoryContext)
    {
        _repositoryContext = repositoryContext;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryService));

    public async Task<GenericResponse<PagedListItem<WebhookDeliveryDto>>> GetDeliveriesAsync(WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveriesAsync));
        try
        {
            _logger.Information("Fetching deliveries with request parameters - {0}", webhookDeliveryQueryRequest);

            DateTimeOffset startDate = webhookDeliveryQueryRequest.StartDate.ToDateTime(TimeOnly.MinValue);
            DateTimeOffset endDate = webhookDeliveryQueryRequest.EndDate.ToDateTime(TimeOnly.MinValue);

            IQueryable<WebhookDelivery> fetchDeliveriesQuery = _repositoryContext.WebhookDeliveries.Where(x => x.CreatedAt >= startDate && x.CreatedAt <= x.CreatedAt);

            if(webhookDeliveryQueryRequest.DeliveryStatus is not null)
            {
                fetchDeliveriesQuery = fetchDeliveriesQuery.Where(x => x.DeliveryStatus == webhookDeliveryQueryRequest.DeliveryStatus);
            }

            if(webhookDeliveryQueryRequest.SubscriptionId.HasValue)
            {
                fetchDeliveriesQuery = fetchDeliveriesQuery.Where(x => x.WebhookSubscriptionEvent.WebhookSubscriptionId == webhookDeliveryQueryRequest.SubscriptionId.Value);
            }

            var totalCount = await fetchDeliveriesQuery.CountAsync(ct);

            List<WebhookDeliveryDto> deliveriesFromDb = await fetchDeliveriesQuery.Select(WebhookDeliveryMapper.ToDtoExpression()).Skip(webhookDeliveryQueryRequest.PageNumber - 1).Take(webhookDeliveryQueryRequest.PageSize).ToListAsync(ct);

            var pagedItem = PagedListItem<WebhookDeliveryDto>.ToPagedListItems(deliveriesFromDb, totalCount, webhookDeliveryQueryRequest.PageSize, webhookDeliveryQueryRequest.PageNumber);

            return GenericResponse<PagedListItem<WebhookDeliveryDto>>.Success(pagedItem, "Webhook deliveries fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching deliveries..");
            return GenericResponse<PagedListItem<WebhookDeliveryDto>>.Failure(null, "An error occurred while fetching webhook deliveries.", HttpStatusCode.InternalServerError);
        }
    }

    public async Task<GenericResponse<WebhookDeliveryDetailsDto>> GetDeliveryDetailsAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryDetailsAsync));
        try
        {
            _logger.Information("Fetching delivery details for - {0}", deliveryId);

            WebhookDeliveryDetailsDto? delivery = await _repositoryContext.WebhookDeliveries.Select(WebhookDeliveryMapper.ToDetailsExpression()).FirstOrDefaultAsync(x => x.Id == deliveryId, ct);

            if(delivery is null)
            {
                _logger.Warning("Webhook delivery with provided id does not exist - {0}", deliveryId);
                return GenericResponse<WebhookDeliveryDetailsDto>.Failure(null, $"Webhook delivery does not exst - {deliveryId}", HttpStatusCode.NotFound);
            }

            _logger.Information("Webhook delivery details fetched successfully - {0}", delivery);
            return GenericResponse<WebhookDeliveryDetailsDto>.Success(delivery, "Webhook delivery details fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching webhook delivery details - {0}", deliveryId);
            return GenericResponse<WebhookDeliveryDetailsDto>.Failure(null, "An error occurred while fetching details.", HttpStatusCode.InternalServerError);
        }
    }
}
