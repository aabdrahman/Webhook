using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Core.RequestParameter;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// <see cref="IWebhookDeliveryService"/> implementation backed by EF Core.
/// </summary>
public class WebhookDeliveryService : IWebhookDeliveryService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    /// <summary>
    /// Initialises a new instance of the <see cref="WebhookDeliveryService"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    /// <param name="authenticatedUserDetails">The current request's caller, used to scope results to their subscriptions.</param>
    public WebhookDeliveryService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryService));

    /// <inheritdoc />
    public async Task<GenericResponse<PagedListItem<WebhookDeliveryDto>>> GetDeliveriesAsync(WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveriesAsync));
        try
        {
            _logger.Information("Fetching deliveries with request parameters - {0}", webhookDeliveryQueryRequest);

            DateTimeOffset startDate = webhookDeliveryQueryRequest.StartDate.ToDateTime(TimeOnly.MinValue);
            DateTimeOffset endDate = webhookDeliveryQueryRequest.EndDate.ToDateTime(TimeOnly.MinValue);

            IQueryable<WebhookDelivery> fetchDeliveriesQuery = _repositoryContext.WebhookDeliveries.OrderByDescending(x => x.CreatedAt).Where(x => x.CreatedAt >= startDate && x.CreatedAt <= endDate);

            if(!_authenticatedUserDetails.assignedRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                if(!Guid.TryParse(_authenticatedUserDetails.userId, out Guid authUserId))
                {
                    _logger.Warning("The authenticated user details could not be parsed as guid - {0}, UserEamil: {1}", _authenticatedUserDetails.userId, _authenticatedUserDetails.emailAddress);
                    return GenericResponse<PagedListItem<WebhookDeliveryDto>>.Failure(null, "An error occurred while performing operation", HttpStatusCode.BadRequest);
                }

                fetchDeliveriesQuery = fetchDeliveriesQuery.Where(x => x.WebhookSubscriptionEvent.webhookSubscription.CreatedByUserId == authUserId);
            }

            if(webhookDeliveryQueryRequest.DeliveryStatus is not null)
            {
                fetchDeliveriesQuery = fetchDeliveriesQuery.Where(x => x.DeliveryStatus == webhookDeliveryQueryRequest.DeliveryStatus);
            }

            if(webhookDeliveryQueryRequest.SubscriptionId.HasValue)
            {
                fetchDeliveriesQuery = fetchDeliveriesQuery.Where(x => x.WebhookSubscriptionEvent.WebhookSubscriptionId == webhookDeliveryQueryRequest.SubscriptionId.Value);
            }

            var totalCount = await fetchDeliveriesQuery.CountAsync(ct);

            List<WebhookDeliveryDto> deliveriesFromDb = await fetchDeliveriesQuery.Select(WebhookDeliveryMapper.ToDtoExpression())
                                                                                    .Skip(webhookDeliveryQueryRequest.PageSize * (webhookDeliveryQueryRequest.PageNumber - 1))
                                                                                    .Take(webhookDeliveryQueryRequest.PageSize)
                                                                                    .ToListAsync(ct);

            var pagedItem = PagedListItem<WebhookDeliveryDto>.ToPagedListItems(deliveriesFromDb, totalCount, webhookDeliveryQueryRequest.PageSize, webhookDeliveryQueryRequest.PageNumber);

            return GenericResponse<PagedListItem<WebhookDeliveryDto>>.Success(pagedItem, "Webhook deliveries fetched successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching deliveries..");
            return GenericResponse<PagedListItem<WebhookDeliveryDto>>.Failure(null, "An error occurred while fetching webhook deliveries.", HttpStatusCode.InternalServerError);
        }
    }

    /// <inheritdoc />
    public async Task<GenericResponse<WebhookDeliveryDetailsDto>> GetDeliveryDetailsAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryDetailsAsync));
        try
        {
            _logger.Information("Fetching delivery details for - {0} for user - {1}", deliveryId, _authenticatedUserDetails.userId);

            var deliveryQuery = _repositoryContext.WebhookDeliveries.AsQueryable();

            if(!_authenticatedUserDetails.assignedRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(_authenticatedUserDetails.userId, out Guid authUserId))
                {
                    _logger.Warning("The authenticated user details could not be parsed as guid - {0}, UserEamil: {1}", _authenticatedUserDetails.userId, _authenticatedUserDetails.emailAddress);
                    return GenericResponse<WebhookDeliveryDetailsDto>.Failure(null, "An error occurred while performing operation", HttpStatusCode.BadRequest);
                }

                deliveryQuery = deliveryQuery.Where(x => x.WebhookSubscriptionEvent.webhookSubscription.CreatedByUserId == authUserId);
            }

            WebhookDeliveryDetailsDto? delivery = await deliveryQuery.Select(WebhookDeliveryMapper.ToDetailsExpression()).FirstOrDefaultAsync(x => x.Id == deliveryId, ct);

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
