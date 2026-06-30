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

/// <summary>
/// Provides operations for managing webhook subscriptions, including creation,
/// retrieval, soft deletion, and reactivation.
/// </summary>
/// <remarks>
/// Each subscription is associated with one or more event types drawn from the
/// <see cref="WebHookEventCatalog"/>. On creation or reactivation a fresh HMAC
/// signing secret is automatically generated via <see cref="ISecretKeyGenerator"/>
/// and stored encrypted via <see cref="IEncryptionService"/>, ensuring the
/// plaintext secret never reaches the database.
/// </remarks>
public sealed class WebhookSubscriptionService : IWebhookSubscriptionService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly ISecretKeyGenerator _secretKeyGenerator;
    private readonly SignatureSecretConfiguration _signatureSecretConfiguration;
    private readonly IEncryptionService _encryptionService;

    /// <summary>
    /// Initializes a new instance of <see cref="WebhookSubscriptionService"/>.
    /// </summary>
    /// <param name="repositoryContext">
    /// The EF Core database context used for all persistence operations.
    /// </param>
    /// <param name="secretKeyGenerator">
    /// Generates a cryptographically random plaintext secret key for each
    /// subscription. The key size is driven by
    /// <paramref name="optionsMonitor"/>.
    /// </param>
    /// <param name="optionsMonitor">
    /// Provides access to <see cref="SignatureSecretConfiguration"/>, which
    /// controls the byte length of generated secret keys. Using
    /// <see cref="IOptionsMonitor{TOptions}"/> means configuration changes are
    /// picked up at runtime without restarting the service.
    /// </param>
    /// <param name="encryptionService">
    /// Encrypts the plaintext secret before it is written to the database,
    /// ensuring secrets are never stored in the clear.
    /// </param>
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

    /// <summary>
    /// Reactivates a previously deactivated webhook subscription and rotates
    /// its signing secret.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses <see cref="EntityFrameworkQueryableExtensions.IgnoreQueryFilters"/>
    /// so that soft-deleted (inactive) subscriptions are still reachable by
    /// their identifier — a global query filter would otherwise exclude them.
    /// </para>
    /// <para>
    /// On successful reactivation a new secret key is generated and encrypted
    /// via <see cref="GenerateAndEncryptSignatureService"/>. This means the
    /// consumer must retrieve the updated secret before sending signed requests.
    /// </para>
    /// </remarks>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the subscription to reactivate.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/> where:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — subscription successfully reactivated.</description></item>
    ///   <item><description><see cref="HttpStatusCode.NotFound"/> — no subscription exists for the provided id (including soft-deleted records).</description></item>
    ///   <item><description><see cref="HttpStatusCode.Conflict"/> — the subscription is already active.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details are captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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

    /// <summary>
    /// Creates a new webhook subscription and associates it with the specified
    /// event types.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All event names supplied in
    /// <see cref="CreateWebhookSubscriptionDto.SubscribedEvents"/> are
    /// normalised to uppercase before being matched against
    /// <c>WebHookEventCatalog.NormalizedEventName</c>. If any event name
    /// cannot be found the entire operation is rejected with
    /// <see cref="HttpStatusCode.BadRequest"/> — partial subscriptions are
    /// not created.
    /// </para>
    /// <para>
    /// A signing secret is generated and encrypted immediately after
    /// validation passes, before any data is written to the database.
    /// The subscription and its associated
    /// <see cref="WebhookSubscriptionEvent"/> join records are persisted in a
    /// single <c>SaveChangesAsync</c> call.
    /// </para>
    /// </remarks>
    /// <param name="createWebhookSubscription">
    /// The details of the subscription to create, including the target
    /// callback URL and the list of event types to subscribe to.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/> where:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.Created"/> — subscription created successfully.</description></item>
    ///   <item><description><see cref="HttpStatusCode.BadRequest"/> — one or more event types do not exist in the catalog.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details are captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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

            List<WebhookSubscriptionEvent> submittedEventsToMap = subscribedEventsInDatabase.Select(x => new WebhookSubscriptionEvent() { WebhookEventCatalogId = x.Id }).ToList();

            subscriptionToInsert.WebhookEvents = submittedEventsToMap;
            

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

    /// <summary>
    /// Soft-deletes a webhook subscription by setting its
    /// <c>IsActive</c> flag to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a soft delete — the subscription record is retained in the
    /// database for audit and history purposes. The associated
    /// <see cref="WebhookSubscriptionEvent"/> records are loaded via
    /// <c>Include</c> so that any cascade behaviour configured on the EF model
    /// is applied correctly during the save.
    /// </para>
    /// <para>
    /// A soft-deleted subscription can be restored by calling
    /// <see cref="ActivateWebhookSubscriptionAsync"/>, which also rotates the
    /// signing secret.
    /// </para>
    /// </remarks>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the subscription to soft-delete.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/> where:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — subscription successfully deactivated.</description></item>
    ///   <item><description><see cref="HttpStatusCode.NotFound"/> — no active subscription exists for the provided id.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details are captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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

    /// <summary>
    /// Retrieves all webhook subscriptions visible to the current query filter.
    /// </summary>
    /// <remarks>
    /// The query uses <c>AsNoTracking</c> for read performance since the
    /// returned DTOs are not intended to be tracked or modified by EF Core.
    /// The projection to <see cref="WebhookSubscriptionDto"/> is performed
    /// server-side via <see cref="WebhookSubscriptionMapper.ToDtoExpression"/>
    /// to avoid loading full entity graphs into memory.
    /// </remarks>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of
    /// <see cref="IReadOnlyList{WebhookSubscriptionDto}"/> where:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — one or more subscriptions returned successfully.</description></item>
    ///   <item><description><see cref="HttpStatusCode.NotFound"/> — no subscriptions exist.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details are captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    public async Task<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>> GetAllWebhookSubscriptionAsync(CancellationToken ct = default)
    {
        _logger = Log.ForContext(_methodName, nameof(GetAllWebhookSubscriptionAsync));
    
        try
        {
            _logger.Information("Fetching all webhook subscriptions....");

            IReadOnlyList<WebhookSubscriptionDto> subscriptions = await _repositoryContext.WebhookSubscriptions.AsNoTracking().Select(WebhookSubscriptionMapper.ToDtoExpression()).ToListAsync(ct);

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

    /// <summary>
    /// Retrieves a single webhook subscription by its unique identifier.
    /// </summary>
    /// <remarks>
    /// The query uses <c>AsNoTracking</c> for read performance and projects
    /// directly to <see cref="WebhookSubscriptionDto"/> server-side via
    /// <see cref="WebhookSubscriptionMapper.ToDtoExpression"/> to avoid
    /// loading unnecessary columns or navigation properties.
    /// </remarks>
    /// <param name="webhookSubscriptionId">
    /// The unique identifier of the webhook subscription to retrieve.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the asynchronous
    /// operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="WebhookSubscriptionDto"/> where:
    /// <list type="bullet">
    ///   <item><description><see cref="HttpStatusCode.OK"/> — subscription found and returned successfully.</description></item>
    ///   <item><description><see cref="HttpStatusCode.NotFound"/> — no subscription exists for the provided id.</description></item>
    ///   <item><description><see cref="HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details are captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
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

    /// <summary>
    /// Generates a cryptographically random signing secret and encrypts it
    /// before returning the ciphertext for storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a two-step pipeline:
    /// <list type="number">
    ///   <item>
    ///     <description>
    ///       <see cref="ISecretKeyGenerator.GenerateKey"/> is called with the
    ///       key size configured in <see cref="SignatureSecretConfiguration.KeySize"/>,
    ///       producing a plaintext secret. The key size controls the entropy of
    ///       the generated value — a larger value produces a stronger secret.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IEncryptionService.Encrypt"/> is called with the
    ///       plaintext secret, returning an encrypted ciphertext string. Only
    ///       this ciphertext is ever written to the database — the plaintext
    ///       secret exists only in memory for the duration of this call.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is called in two places:
    /// <list type="bullet">
    ///   <item><description><see cref="CreateWebhookSubscriptionAsync"/> — sets the initial secret on a new subscription.</description></item>
    ///   <item><description><see cref="ActivateWebhookSubscriptionAsync"/> — rotates the secret when a subscription is reactivated.</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <returns>
    /// The AES-encrypted ciphertext of the generated secret key, ready to be
    /// stored in <c>WebhookSubscription.SecretKey</c>.
    /// </returns>
    private string GenerateAndEncryptSignatureService()
    {
        var plainSignatureSecret = _secretKeyGenerator.GenerateKey(_signatureSecretConfiguration.KeySize);
        return _encryptionService.Encrypt(plainSignatureSecret);
    }
}
