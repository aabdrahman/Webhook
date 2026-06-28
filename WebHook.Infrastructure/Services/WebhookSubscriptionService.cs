using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class WebhookSubscriptionService : IWebhookSubscriptionService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly ISecretKeyGenerator _secretKeyGenerator;
    private readonly SignatureSecretConfiguration _signatureSecretConfiguration;
    private readonly IEncryptionService _encryptionService;

    public WebhookSubscriptionService(RepositoryContext repositoryContext, ISecretKeyGenerator secretKeyGenerator, 
                                        IOptionsMonitor<SignatureSecretConfiguration> optionsMonitor, IEncryptionService encryptionService)
    {
        _logger = Log.ForContext(_className, nameof(WebhookSubscriptionService));
        _repositoryContext = repositoryContext;
        _secretKeyGenerator = secretKeyGenerator;
        _signatureSecretConfiguration = optionsMonitor.CurrentValue;
        _encryptionService = encryptionService;
    }

    private string _className = "ClassName";
    private string _methodName = "MethodName";
    private ILogger _logger;

    public async Task<GenericResponse<string>> ActivateWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(ActivateWebhookSubscriptionAsync));

        try
        {
            _logger.Information("Activate deleted webhook subscription - {0}", webhookSubscriptionId);

            WebhookSubscription? webhookSubscriptionToActivate = await _repositoryContext.WebhookSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == webhookSubscriptionId, ct);

            if(webhookSubscriptionToActivate is null)
            {
                _logger.Information("Webhook subscription with provided id does not exist - {0}", webhookSubscriptionId);
                return GenericResponse<string>.Failure("Operation Failed.", "Webhook subscription with provided id deos not exist.", HttpStatusCode.NotFound);
            }

            if (webhookSubscriptionToActivate.IsActive)
            {
                _logger.Information("Webhook with Id: {0} is currently active.", webhookSubscriptionId);
                return GenericResponse<string>.Failure("Operation Failed.", "Webhook is currently active.", HttpStatusCode.Conflict);
            }

            webhookSubscriptionToActivate.UpdatedAt = DateTimeOffset.UtcNow;
            webhookSubscriptionToActivate.IsActive = true;
            webhookSubscriptionToActivate.SecretKey = GenerateAndEncryptSignatureService();

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Webhook subscription with provided id: {0} successfully reactivated.", webhookSubscriptionToActivate.Id);

            return GenericResponse<string>.Success("Operation Successful.", "Webhook subscription successfully activated.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occrred activating webhook subscription - {0}", webhookSubscriptionId);
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred activating webhook sunscription.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
       
        }
    }

    public async Task<GenericResponse<string>> CreateWebhookSubscriptionAsync(CreateWebhookSubscriptionDto createWebhookSubscription, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(CreateWebhookSubscriptionAsync));

        try
        {
            _logger.Information("Create Webhook Subscription with details - {0}", createWebhookSubscription);

            //Validate and check the provided subscribed events
            var eventsToSubscribe = createWebhookSubscription.SubscribedEvents.Select(x => x.ToUpper()).ToList();
            var subscribedEventsInDatabase = await _repositoryContext.WebHookEventCatalogs
                                                            .Where(x => eventsToSubscribe.Contains(x.NormalizedEventName))
                                                            .Select(x => new { x.Id, x.NormalizedEventName })
                                                            .ToListAsync(ct);
            var notExistingEvents = eventsToSubscribe.Except(subscribedEventsInDatabase.Select(x => x.NormalizedEventName).ToList()).ToList();

            if(eventsToSubscribe.Count != subscribedEventsInDatabase.Count)
            {
                _logger.Information("Events to subscribe - {0} does not exist in the database.", string.Join(", ", notExistingEvents));
                return GenericResponse<string>.Failure("Operation Failed.", "One or more events to subscribe does not exist.", HttpStatusCode.BadRequest);
            }

            //Begin insertion operations
            WebhookSubscription subscriptionToInsert = createWebhookSubscription.ToEntity();
            subscriptionToInsert.SecretKey = GenerateAndEncryptSignatureService();

            await _repositoryContext.WebhookSubscriptions.AddAsync(subscriptionToInsert, ct);

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Webhook Subscription successful - {0}", subscriptionToInsert.Id);

            return GenericResponse<string>.Success("Operation Successful.", "You have successfully subscribed to teh webhook.", HttpStatusCode.Created);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occrred while creating webhook subscription.");
            return GenericResponse<string>.Failure("Operation Failed", "An error occurred when creating webhook subscription.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });

        }

    }

    public async Task<GenericResponse<string>> DeleteWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(DeleteWebhookSubscriptionAsync));

        try
        {
            _logger.Information("Deleting webhook subscription - {0}", webhookSubscriptionId);

            WebhookSubscription? webhookSubscriptionToDelete = await _repositoryContext.WebhookSubscriptions.Include(x => x.WebhookEvents).FirstOrDefaultAsync(x => x.Id == webhookSubscriptionId, ct);

            if(webhookSubscriptionToDelete is null)
            {
                _logger.Information("Webhook to delete with provided Id does not exist - {0}", webhookSubscriptionId);
                return GenericResponse<string>.Failure("Operation Failed.", "Webhook with provided id does not exist.", HttpStatusCode.NotFound);
            }

            webhookSubscriptionToDelete.IsActive = false;

            await _repositoryContext.SaveChangesAsync(ct);

            _logger.Information("Webhook with Id: {0} successfully deleted.", webhookSubscriptionId);

            return GenericResponse<string>.Success("Operation Successful.", "Webhook suv=bscription successfully deleted.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred performing webhook subscription delete operation.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred deleting webhook.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    public async Task<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>> GetAllWebhookSubscriptionAsync(CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetAllWebhookSubscriptionAsync));
    
        try
        {
            _logger.Information("Fetching all webhook subscriptions....");

            IReadOnlyList<WebhookSubscriptionDto> subscriptions = await _repositoryContext.WebhookSubscriptions.AsNoTracking().Select(WebhookSubscriptionMapper.ToDtoExpression()).ToListAsync();

            _logger.Information("Webhook Subscriptions fetched successfully - {0}", subscriptions);

            return subscriptions.Any() ? GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>.Success(subscriptions, "Webhook Subscriptions fetched successfully.", HttpStatusCode.OK) :
                GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>.Failure(null, "No webhook subscription fetched.", HttpStatusCode.NotFound);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching webhook subscriptions...");
            return GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>.Failure(null, "An error occurred fetching webhook subscriptions.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorDescription = ex?.InnerException?.Message ?? "", ErrorMessage = ex.Message, ErrorTitle = ex.GetType().Name });

        }

    }

    public async Task<GenericResponse<WebhookSubscriptionDto>> GetWebhookSubscriptionByIdAsync(Guid webhookSubscriptionId, CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetWebhookSubscriptionByIdAsync));

        try
        {
            _logger.Information("Fetching web hook subscription for id - {0}", webhookSubscriptionId);

            WebhookSubscriptionDto? webhookSubscription = await _repositoryContext.WebhookSubscriptions.AsNoTracking().Select(WebhookSubscriptionMapper.ToDtoExpression()).FirstOrDefaultAsync(x => x.Id == webhookSubscriptionId, ct);

            if(webhookSubscription is null)
            {
                _logger.Information("Webhook subscription with the id does no exist - {0}", webhookSubscriptionId);
                return GenericResponse<WebhookSubscriptionDto>.Failure(null, "Webhook SUv=bscription with provided id does not exist.", HttpStatusCode.NotFound);
            }

            _logger.Information("Webhook subscription fetced successfully - {0}, {1}", webhookSubscriptionId, webhookSubscription);
            return GenericResponse<WebhookSubscriptionDto>.Success(webhookSubscription, "Webhook Subscription Fetched Successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred fetching webhook subscription.");
            return GenericResponse<WebhookSubscriptionDto>.Failure(null, "An error occurred fetching webhook subscription.", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }

    }

    private string GenerateAndEncryptSignatureService()
    {
        var plainSignatureSecret = _secretKeyGenerator.GenerateKey(_signatureSecretConfiguration.KeySize);
        return _encryptionService.Encrypt(plainSignatureSecret);
    }
}
