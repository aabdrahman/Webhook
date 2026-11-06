using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

/// <summary>
/// Starts callback URL verification for a subscription and lists its verification history.
/// </summary>
/// <remarks>
/// Starting a verification stores a random challenge token and queues it for
/// <see cref="SubscriptionVerificationProcessorService"/>, which performs the handshake. Users can only start or
/// read verifications for subscriptions they created; admins can read any.
/// </remarks>
public class SubscriptionVerificationService : ISubscriptionVerificationService
{
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;
    private readonly RepositoryContext _repositoryContext;
    private readonly ISecretKeyGenerator _secretKeyGenerator;
    private readonly IApplicationPublisher _applicationPublisher;

    /// <summary>
    /// Initialises a new instance of the <see cref="SubscriptionVerificationService"/> class.
    /// </summary>
    /// <param name="authenticatedUserDetails">The caller of the current request.</param>
    /// <param name="repositoryContext">The database context.</param>
    /// <param name="secretKeyGenerator">Generates the challenge token.</param>
    /// <param name="applicationPublisher">Queues the verification for the processor worker.</param>
    public SubscriptionVerificationService(IAuthenticatedUserDetails authenticatedUserDetails, RepositoryContext repositoryContext,
                                            ISecretKeyGenerator secretKeyGenerator, IApplicationPublisher applicationPublisher)
    {
        _authenticatedUserDetails = authenticatedUserDetails;
        _repositoryContext = repositoryContext;
        _secretKeyGenerator = secretKeyGenerator;
        _applicationPublisher = applicationPublisher;
    }

    private ILogger _logger = Log.ForContext("ClassName", nameof(SubscriptionVerificationService));
    private const string _methodName = "MethodName";

    /// <inheritdoc />
    /// <remarks>
    /// Fails with <c>404</c> if the caller did not create the subscription, <c>409</c> if the subscription is not
    /// <c>Inactive</c> or a verification is already in progress, and <c>503</c> if the verification could not be queued.
    /// </remarks>
    public async Task<GenericResponse<string>> VerifySubscriptionCallbackUrl(Guid SubscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(VerifySubscriptionCallbackUrl));

        try
        {
            _logger.Information("Begin Callback URL verification for subscription - {0}", SubscriptionId);

            if(!Guid.TryParse(_authenticatedUserDetails.userId, out var userId))
            {
                _logger.Error("Invalid user id - {0}", _authenticatedUserDetails.userId);
                return GenericResponse<string>.Failure("Operation Failed.", "An error occurred processing request.", HttpStatusCode.BadRequest);
            }

            var subscription = await _repositoryContext.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == SubscriptionId && x.CreatedByUserId == userId, ct);

            if(subscription is null)
            {
                _logger.Error("Subscription with id: {0} not found for user - {1}", SubscriptionId, userId);
                return GenericResponse<string>.Failure("Operation Failed.", "Subscription not found.", HttpStatusCode.NotFound);
            }

            if(subscription.OnboardStatus != SubscriptionOnboardStatus.Inactive)
            {
                _logger.Error("Subscription with id: {0} is not in inactive status for user - {1}", SubscriptionId, userId);
                return GenericResponse<string>.Failure("Operation Failed.", "Subscription is not in inactive status.", HttpStatusCode.Conflict);
            }

            //A pending verification older than the challenge validity can no longer be answered, so it does not block a new one.
            var pendingSince = DateTimeOffset.UtcNow - SubscriptionVerificationProcessorService.ChallengeValidity;
            bool verificationInProgress = await _repositoryContext.WebhookSubscriptionVerifications
                                            .AnyAsync(x => x.SubscriptionId == subscription.Id && x.Status == WebhookSubscriptionVerificationStatus.Pending && x.CreatedAt > pendingSince, ct);

            if (verificationInProgress)
            {
                _logger.Warning("A verification is already in progress for subscription - {0}", SubscriptionId);
                return GenericResponse<string>.Failure("Operation Failed.", "A verification for this subscription is already in progress.", HttpStatusCode.Conflict);
            }

            var signatureVerificationRecordToInsert = new WebhookSubscriptionVerification
            {
                SubscriptionId = subscription.Id,
                Status = WebhookSubscriptionVerificationStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow,
                ChallengeToken = _secretKeyGenerator.GenerateKey(secretKeySize: 64)
            };

            await _repositoryContext.WebhookSubscriptionVerifications.AddAsync(signatureVerificationRecordToInsert, ct);

            await _repositoryContext.SaveChangesAsync(ct);

            var queueResult = await _applicationPublisher.QueueSignatureVerificationEvent(new SubscriptionVerificationRaised(signatureVerificationRecordToInsert.Id), ct);

            if (!queueResult)
            {
                // Verification could not be queued, so mark it as failed and return 503 to the caller.
                _logger.Error("Signature verification {0} for subscription {1} could not be queued.", signatureVerificationRecordToInsert.Id, SubscriptionId);

                signatureVerificationRecordToInsert.Status = WebhookSubscriptionVerificationStatus.Failed;
                signatureVerificationRecordToInsert.ResponseDetails = "The verification could not be queued.";
                await _repositoryContext.SaveChangesAsync(ct);

                return GenericResponse<string>.Failure("Operation Failed.", "The verification could not be started. Please try again later.", HttpStatusCode.ServiceUnavailable);
            }

            _logger.Information("Signature verification record created and queued successfully for subscription - {0}", SubscriptionId);

            return GenericResponse<string>.Success("Operation Successful.", "Subscription callback URL verification initiated successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while verifying subscription callback URL for subscription - {0}", SubscriptionId);
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred processing request.", HttpStatusCode.InternalServerError);
        }

    }

    /// <inheritdoc />
    /// <remarks>
    /// Unless the caller is an admin, the subscription must have been created by the caller; otherwise the result is <c>404</c>.
    /// </remarks>
    public async Task<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>> GetSubscriptionVerifications(Guid SubscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetSubscriptionVerifications));

        try
        {
            _logger.Information("Fetching subscription verification records for - {0}", SubscriptionId);

            bool isAdmin = string.Equals(_authenticatedUserDetails.assignedRole, "Admin", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin)
            {
                if (!Guid.TryParse(_authenticatedUserDetails.userId, out var userId))
                {
                    _logger.Error("Invalid user id - {0}", _authenticatedUserDetails.userId);
                    return GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Failure(null, "An error occurred processing request.", HttpStatusCode.BadRequest);
                }

                bool ownsSubscription = await _repositoryContext.WebhookSubscriptions.AnyAsync(x => x.Id == SubscriptionId && x.CreatedByUserId == userId, ct);

                if (!ownsSubscription)
                {
                    _logger.Warning("Subscription {0} not found for user {1} when fetching verification records.", SubscriptionId, userId);
                    return GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Failure(null, "Subscription not found.", HttpStatusCode.NotFound);
                }
            }

            var verificationRecords = await _repositoryContext.WebhookSubscriptionVerifications
                                        .Where(x => x.SubscriptionId == SubscriptionId)
                                        .OrderByDescending(x => x.CreatedAt)
                                        .Select(x => new SubscriptionVerificationDto(x.Id, x.CreatedAt, x.AttemptedAt, x.Status.ToString()))
                                        .ToListAsync(ct);

            _logger.Information("Fetched {0} subscription verification records for - {1}", verificationRecords.Count, SubscriptionId);

            return GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Success(verificationRecords, "Subscription verification records fetched successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching subscription verification records for subscription - {0}", SubscriptionId);
            return GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Failure(null, "An error occurred processing request.", HttpStatusCode.InternalServerError);
        }

    }
}
