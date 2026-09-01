using MassTransit.Initializers;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using System.Text;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookServiceClientService : IWebhookServiceClientService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;
    private readonly ICacheService _cacheService;
    private readonly IApplicationHasher _applicationHasher;

    public WebhookServiceClientService(RepositoryContext repositoryContext, IAuthenticatedUserDetails authenticatedUserDetails, ICacheService cacheService, IApplicationHasher applicationHasher)
    {
        _repositoryContext = repositoryContext;
        _authenticatedUserDetails = authenticatedUserDetails;
        _cacheService = cacheService;
        _applicationHasher = applicationHasher;
    }

    private const string _methodName = "MethodName";
    private const string _className = "ClassName";
    private ILogger _logger = Log.ForContext(_className, nameof(WebhookServiceClientService));

    public async Task<GenericResponse<string>> DeactivateClientAsync(string clientId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(DeactivateClientAsync));

        try
        {
            _logger.Information("Deactivating onboaded client - {0}", clientId);

            bool isAuthenticatedUserExist = await _repositoryContext.Users.AnyAsync(x => x.NormalizedEmail == _authenticatedUserDetails.emailAddress, ct);
            if (!isAuthenticatedUserExist)
            {
                await _cacheService.RemoveItemsFromCacheAsync(_authenticatedUserDetails.emailAddress);
                _logger.Warning("Authenticated user with provided details does not exist - {0}, {1}", _authenticatedUserDetails.emailAddress, _authenticatedUserDetails.userId);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials.", HttpStatusCode.Forbidden);
            }

            WebhookServiceClient? clientToDeactivate = await _repositoryContext.WebhookServiceClients.Include(x => x.EventCatalogs).FirstOrDefaultAsync(x => x.ClientId == clientId.ToLower(), ct);

            if(clientToDeactivate is null)
            {
                _logger.Warning("No webhook client with the provided client id - {0}", clientId);
                return GenericResponse<string>.Failure("Operation Failed.", $"Service client with provided id does not exist: {clientId}", HttpStatusCode.NotFound);
            }

            DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;
            clientToDeactivate.IsActive = false;
            clientToDeactivate.DeactivatedAt = operationTimestamp;
            clientToDeactivate.DeactivatedBy = _authenticatedUserDetails.userId;
            foreach (var subEvent in clientToDeactivate.EventCatalogs)
            {
                subEvent.DeactivatedAt = operationTimestamp;
                subEvent.DeactivatedBy = _authenticatedUserDetails.userId;
            }

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Service cleint: {0} deactivated successfully. All subscribed catalogs also deactivated.", clientToDeactivate.ClientId);
            return GenericResponse<string>.Success("Operation Successful.", "Service client successfully deactivated.", HttpStatusCode.NoContent);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while deactivating client - {0}", clientId);
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while deactivating service client.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "", ErrorMessage = ex.Message });
        }
    }

    public async Task<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>> GetAllClientsAsync(bool includeDeactivated = false, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetAllClientsAsync));

        try
        {
            _logger.Information("Get All Onboarded Clients. Include Deactivated - {0}", includeDeactivated);

            var getClientsQueryable = includeDeactivated ?  _repositoryContext.WebhookServiceClients.IgnoreQueryFilters() : _repositoryContext.WebhookServiceClients;

            List<WebhookServiceClientDto> onboardedClients = await getClientsQueryable.Select(WebhookServiceClientMapper.ToDtoExpression()).ToListAsync(ct);

            _logger.Information("Onboarded cleints fetch reaturns {0} row(s).", onboardedClients.Count);
            return onboardedClients.Any() ?
                GenericResponse<IReadOnlyList<WebhookServiceClientDto>>.Success(onboardedClients, "Onboarded clients fetched successfully.", HttpStatusCode.OK) :
                GenericResponse<IReadOnlyList<WebhookServiceClientDto>>.Failure(null, "No client has been onboarded yet.", HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while getting onboarded clients.");
            return GenericResponse<IReadOnlyList<WebhookServiceClientDto>>.Failure(null, "An error occurred while getting onboarded clients.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "", ErrorMessage = ex.Message });
        }

    }

    public async Task<GenericResponse<WebhookServiceClientDto>> GetByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetByClientIdAsync));

        try
        {
            _logger.Information("Get client details with id - {0}", clientId);

            WebhookServiceClientDto? onboardedClient = await _repositoryContext.WebhookServiceClients.Select(WebhookServiceClientMapper.ToDtoExpression()).FirstOrDefaultAsync(x => x.ClientId == clientId.ToLower(), ct);

            if(onboardedClient is null)
            {
                _logger.Error("Client with provided id does not exist or has been deactivated - {0}", clientId);
                return GenericResponse<WebhookServiceClientDto>.Failure(null, $"Service client with provided id does not exist or has been deactivated - {clientId}", HttpStatusCode.NotFound);
            }

            _logger.Information("Onboarded client with id: {0} fetched successfully - {1}", clientId, onboardedClient);
            return GenericResponse<WebhookServiceClientDto>.Success(onboardedClient, "Client fetched successfully", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetchin onboarded client deatils.");
            return GenericResponse<WebhookServiceClientDto>.Failure(null, "An error occurred whule getting onboarded client details.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "", ErrorMessage = ex.Message });
        }
    }

    public async Task<GenericResponse<ServiceClientOnboardingResponse>> OnboardNewServiceClientAsync(CreateServiceClientDto createServiceClient, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(OnboardNewServiceClientAsync));

        try
        {
            _logger.Information("Onboard New Service Client - {0}", createServiceClient);

            bool isExistClientId = await _repositoryContext.WebhookServiceClients.AsNoTracking().AnyAsync(x => x.ClientId == createServiceClient.ClientId.ToLower(), ct);
            if (isExistClientId)
            {
                _logger.Warning("Service client with provided client id already exist - {0}", createServiceClient.ClientId);
                return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, $"Service Client already onboarded - {createServiceClient.ClientId}", HttpStatusCode.Conflict);
            }

            var eventCatalogToSubscribe = createServiceClient.AllowedEventTypes.Select(x => x.ToUpper()).ToList();
            var catalogsFromDb = await _repositoryContext.WebHookEventCatalogs
                                                .AsNoTracking()
                                                .Where(x => eventCatalogToSubscribe.Contains(x.NormalizedEventName))
                                                .Select(x => new { x.Id, x.NormalizedEventName })
                                                .ToListAsync(ct);

            var notExistingCatalogs = eventCatalogToSubscribe.Except(catalogsFromDb.Select(x => x.NormalizedEventName)).ToList();
            if (notExistingCatalogs.Any())
            {
                _logger.Warning("One or more provided catalog to subscribe does not exist or deactivated - {0}", notExistingCatalogs);
                return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, $"One or more provided event catalog(s) does not exist - {string.Join(", ", notExistingCatalogs)}", HttpStatusCode.NotFound);
            }

            var isUserExist = await _repositoryContext.Users.AnyAsync(x => x.NormalizedEmail == _authenticatedUserDetails.emailAddress.ToUpper(), ct);
            if (!isUserExist)
            {
                await _cacheService.RemoveItemsFromCacheAsync(_authenticatedUserDetails.emailAddress);
                _logger.Warning("The provided authenticated user email is either deactivated or deleted or does not exist - {0}, {1}. Operation will remove the cached token.", _authenticatedUserDetails.emailAddress, _authenticatedUserDetails.userId);
                return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, "Unauthorized Access.", HttpStatusCode.Forbidden);
            }

            string clientKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(Random.Shared.GetHexString(16)));
            if (string.IsNullOrEmpty(clientKey))
            {
                _logger.Warning("The application could not generate a valid secret key for the client.");
                return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, "Error occured while performing operation.", HttpStatusCode.BadRequest);
            }

            WebhookServiceClient webhookServiceClient = createServiceClient.ToEntity();
            webhookServiceClient.CreatedBy = _authenticatedUserDetails.userId;
            webhookServiceClient.ClientKey = await _applicationHasher.HashSecret(clientKey);
            foreach (var item in catalogsFromDb)
            {
                webhookServiceClient.EventCatalogs.Add
                (
                    new WebhookServiceClientEventCatalog()
                    {
                        CreatedAt = webhookServiceClient.CreatedAt,
                        EventCatalogId = item.Id
                    }
                );
            }
           

            await _repositoryContext.WebhookServiceClients.AddAsync(webhookServiceClient);
            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Service Client onboarded successfully - {0}, {1}", createServiceClient.ServiceName, createServiceClient.ClientId);
            return GenericResponse<ServiceClientOnboardingResponse>.Success(new ServiceClientOnboardingResponse() { ClientId = webhookServiceClient.ClientId, ClientKey = webhookServiceClient.ClientKey }, "Service Client Onboarded successfully.", HttpStatusCode.Created);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while onboarding client - {0}", createServiceClient.ServiceName);
            return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, "An error occurred onboarding client.", HttpStatusCode.InternalServerError, new ErrorDetail()
            {
                ErrorMessage = ex.Message,
                ErrorTitle = ex.GetType().Name,
                ErrorDescription = ex.InnerException?.Message ?? ""
            });
        }
    }

    public async Task<GenericResponse<string>> ReactivateClientAsync(string clientId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ReactivateClientAsync));

        try
        {
            _logger.Information("Reactivate Sevrice Client with id - {0}", clientId);

            WebhookServiceClient? clientToReactivate = await _repositoryContext.WebhookServiceClients.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.ClientId == clientId.ToLower(), ct);

            if(clientToReactivate is null)
            {
                _logger.Warning("Service client with the provided id does not exist - {0}", clientId);
                return GenericResponse<string>.Failure("Operation Failed.", $"Service client with provided id does not exist - {clientId}", HttpStatusCode.NotFound);
            }

            if (clientToReactivate.IsActive)
            {
                _logger.Warning("Service client with provided id: {0} is already active.", clientId);
                return GenericResponse<string>.Failure("Operation Failed.", "Service client is already active.", HttpStatusCode.Conflict);
            }

            clientToReactivate.IsActive = true;
            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Service client reactivated successfully - {0}, {1}.", clientToReactivate.ClientId, clientToReactivate.Id);
            return GenericResponse<string>.Success("Operation Successful.", "Onboarded client reactivated successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occirred while reactivating service client - {0}", clientId);
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while reactivating onboarded client.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "", ErrorMessage = ex.Message });
            
        }
    }

    public async Task<GenericResponse<ServiceClientOnboardingResponse>> RequestNewClientKeyAsync(RequestNewClientKeyDto requestNewClientKey, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RequestNewClientKeyAsync));

        try
        {
            _logger.Information("Request new client key request - {0}", requestNewClientKey);

            WebhookServiceClient? clientToUpdate = await _repositoryContext.WebhookServiceClients.FirstOrDefaultAsync(x => x.ClientId == requestNewClientKey.ClientId.ToLower(), ct);

            if(clientToUpdate is null)
            {
                _logger.Warning("Service client with provided id does not exist - {0}", requestNewClientKey.ClientId);
                return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, $"Service client does not exist: {requestNewClientKey.ClientId}", HttpStatusCode.NotFound);
            }

            string newClientKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(Random.Shared.GetHexString(16)));

            clientToUpdate.ClientKey = await _applicationHasher.HashSecret(newClientKey);

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Service client key updated successfully for client - {0}", clientToUpdate.ClientId);

            return GenericResponse<ServiceClientOnboardingResponse>.Success(new ServiceClientOnboardingResponse() { ClientId = clientToUpdate.ClientId, ClientKey = newClientKey }, "Client Key updated successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while performing client key update.");
            return GenericResponse<ServiceClientOnboardingResponse>.Failure(null, "An error occurred while performing operation.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }
}
