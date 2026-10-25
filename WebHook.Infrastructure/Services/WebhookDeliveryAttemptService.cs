using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// <see cref="IWebhookDeliveryAttemptService"/> implementation backed by EF Core.
/// </summary>
public class WebhookDeliveryAttemptService : IWebhookDeliveryAttemptService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    /// <summary>
    /// Initialises a new instance of the <see cref="WebhookDeliveryAttemptService"/> class.
    /// </summary>
    /// <param name="repositoryContext">The database context.</param>
    public WebhookDeliveryAttemptService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
    }

    private const string _methodName = "MethodName";
    private ILogger _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryAttemptService));

    /// <inheritdoc />
    public async Task<GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>> GetDeliveryAttemptsAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryAttemptsAsync));
        try
        {
            _logger.Information("Fetching delivery attempts for - {0} for user - {1}", deliveryId, _authenticatedUserDetails.userId);

            var deliveryAttemptsQuery = _repositoryContext.WebhookDeliveryAttempts
                .Where(x => x.WebhookDeliveryId == deliveryId)
                .OrderByDescending(x => x.AttemptedAt);
             
            if(!_authenticatedUserDetails.assignedRole.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                if (!Guid.TryParse(_authenticatedUserDetails.userId, out Guid authUserId))
                {
                    _logger.Warning("The authenticated user details could not be parsed as guid - {0}, UserEamil: {1}", _authenticatedUserDetails.userId, _authenticatedUserDetails.emailAddress);
                    return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Failure(null, "An error occurred while performing operation", HttpStatusCode.BadRequest);
                }

                deliveryAttemptsQuery = _repositoryContext.WebhookDeliveries.Where(x => x.WebhookSubscriptionEvent.webhookSubscription.CreatedByUserId == authUserId && x.Id == deliveryId)
                    .SelectMany(x => x.WebhookDeliveryAttempts)
                    .OrderByDescending(x => x.AttemptedAt);
            }

            List<WebhookDeliveryAttemptDto> deliveryAttempts = await deliveryAttemptsQuery.Select(WebhookDeliveryAttemptMapper.ToDtoExpression()).ToListAsync(ct);

            if(!deliveryAttempts.Any())
            {
                _logger.Warning("No delivery attempts yet for delivery - {0}", deliveryId);
                return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Failure(null, "Webhook delivery attempts not yet made for the delievry.", HttpStatusCode.NotFound);
            }

            _logger.Information("Delivery attempts fetched successfully - {0}", deliveryAttempts);
            return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Success(deliveryAttempts, "Webhook delivery attempts fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching delivery attempts for delievry - {0}", deliveryId);
            return GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>.Failure(null, "An error occurrd while fetching delivery attempts.", HttpStatusCode.InternalServerError);
        }
    }
}