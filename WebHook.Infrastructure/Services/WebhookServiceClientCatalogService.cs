using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookServiceClientCatalogService : IWebhookServiceClientCatalogService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    public WebhookServiceClientCatalogService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
    }

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";
    private ILogger _logger = Log.ForContext(_className, nameof(WebhookServiceClientCatalogService));

    public async Task<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>> GetSubscribedCatalogsAsync(Guid serviceClientId, bool includeDeactivated = false, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetSubscribedCatalogsAsync));

        try
        {
            _logger.Information("Getting subscribed catalog for client - {0}", serviceClientId);

            var dbQueryable = includeDeactivated ? 
                                    _repositoryContext.WebhookServiceClientEventCatalogs.IgnoreQueryFilters() : 
                                    _repositoryContext.WebhookServiceClientEventCatalogs;

            List<WebhookServiceClientCatalogDto> serviceClientCatalogs = await dbQueryable
                                                                                .Where(x => x.ServiceClientId == serviceClientId)
                                                                                .Select(WebhookServiceClientCatalogMapper.ToDtoExpression()).ToListAsync(ct);

            _logger.Information("Subscribed event catalogs fetched successfully - {0}", serviceClientCatalogs);

            return serviceClientCatalogs.Any() ?
                GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>.Success(serviceClientCatalogs, "Subscribed event catalogs fetched successfully.", HttpStatusCode.OK) :
                GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>.Failure(null, $"No subscribed event catalog for provided id - {serviceClientId}", HttpStatusCode.NotFound);
                //GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>.Success(serviceClientCatalogs, "Subscribed event catalogs fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting subscribed catalogs for service client.");
            return GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>.Failure(null, "An error occurred while fetching subscribed catalogs.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    public async Task<GenericResponse<string>> SubscribeToCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(SubscribeToCatalogAsync));

        try
        {
            _logger.Information("Subscribe to catalog request - {0},{1}", serviceClientId, catalogName);

            //Check if the event caatlog already exists for catalog
            WebhookServiceClientEventCatalog? existingCatalog = await _repositoryContext.WebhookServiceClientEventCatalogs.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ServiceClientId == serviceClientId && x.eventCatalog.NormalizedEventName == catalogName.ToUpper(), ct);

            //Check if there is an existing catalog with provided details.
            if (existingCatalog is not null)
            {
                //Check if its been deactivated and then re-activate it
                if (existingCatalog.DeactivatedAt.HasValue)
                {
                    existingCatalog.DeactivatedAt = null;
                    existingCatalog.DeactivatedBy = string.Empty;
                }
                else if (!existingCatalog.DeactivatedAt.HasValue)
                {
                    _logger.Information("Service Client already subscribed to the catalog - {0}, {1}", serviceClientId, catalogName);
                    return GenericResponse<string>.Failure("Operation Failed.", $"Catalog has already been subscribed to: {catalogName}", HttpStatusCode.Conflict);
                }

                //Update and maintain details to the database
                await _repositoryContext.SaveChangesAsync(ct);
                _logger.Information("Subscribed service client catalog has been reactivated successfully - {0}", existingCatalog.Id);
                return GenericResponse<string>.Success("Operation Successful.", "Subscribed event catalog has been reactivated successfully.", HttpStatusCode.OK);
            }
            else
            {
                _logger.Information("Begin fresh insert of a new subsciption for service client.");
                //The event catalog does not exist for service client.
                WebHookEventCatalog? catalogToSubscribe = await _repositoryContext.WebHookEventCatalogs.FirstOrDefaultAsync(x => x.NormalizedEventName == catalogName.ToUpper(), ct);

                //Check if catalog to subscribe does not exist
                if (catalogToSubscribe is null)
                {
                    _logger.Warning("The provided catalog name does not exist - {0}", catalogName);
                    return GenericResponse<string>.Failure("Operation Failed.", $"Event Catalog to subscribe does not exist: {catalogName}", HttpStatusCode.NotFound);
                }

                //Check that the serice client id exists
                WebhookServiceClient? serviceClient = await _repositoryContext.WebhookServiceClients.FirstOrDefaultAsync(x => x.Id == serviceClientId, ct);
                if (serviceClient is null)
                {
                    _logger.Warning("Service client with provided id does not exist - {0}", serviceClientId);
                    return GenericResponse<string>.Failure("Operation Failed.", $"Service client with provided id does not exist: {serviceClientId}", HttpStatusCode.NotFound);
                }

                //Add the catalog-service-client entity.
                serviceClient.EventCatalogs.Add
                (
                    new WebhookServiceClientEventCatalog()
                    {
                        EventCatalogId = catalogToSubscribe.Id
                    }
                );

                //Save chanegs to teh database
                await _repositoryContext.SaveChangesAsync(ct);

                _logger.Information("Service client subsctiption added successfully - {0}, {1}. Created By: {2}", serviceClientId, catalogName, _authenticatedUserDetails.userId);
                return GenericResponse<string>.Success("Operation Successful.", "Catalog has been subscribed to successfully.", HttpStatusCode.OK);

            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while subscribing to catalog.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while performing operation.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    public async Task<GenericResponse<string>> UnSubscribeFromCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(UnSubscribeFromCatalogAsync));

        try
        {
            _logger.Information("Unsubscribe client: {0} from even catalog: {1}", serviceClientId, catalogName);

            WebhookServiceClientEventCatalog? clientCatalogToUnsubscribe = await _repositoryContext.WebhookServiceClientEventCatalogs.SingleOrDefaultAsync(x => x.ServiceClientId == serviceClientId && x.eventCatalog.NormalizedEventName == catalogName.ToUpper(), ct);

            if (clientCatalogToUnsubscribe is null)
            {
                _logger.Warning("Client Catalog with details does not exist - {0}, {1}", serviceClientId, catalogName);
                return GenericResponse<string>.Failure("Operation Failed.", "Catalog does not exist for provided client.", HttpStatusCode.NotFound);
            }

            clientCatalogToUnsubscribe.DeactivatedAt = DateTimeOffset.UtcNow;
            clientCatalogToUnsubscribe.DeactivatedBy = _authenticatedUserDetails.userId;

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Event catalog unsubscribed successfully from user - {0}, {1}", serviceClientId, catalogName);
            return GenericResponse<string>.Success("Operation Successful.", "Catalog unsubscribed from succssfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while unsubscribing from catalog.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while unsubscribing from catalog.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }
}
