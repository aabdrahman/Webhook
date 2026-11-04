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

public class SubscriptionVerificationService : ISubscriptionVerificationService
{
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;
    private readonly RepositoryContext _repositoryContext;
    private readonly ISecretKeyGenerator _secretKeyGenerator;
    private readonly IApplicationPublisher _applicationPublisher
        ;

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

    public async Task<GenericResponse<string>> VerifySubscriptionCallbackUrl(Guid SubscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(VerifySubscriptionCallbackUrl));

        try
        {
            _logger.Information("Begin Callback URL verification for subsciption - {0|", SubscriptionId);

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

            var signatureVerificationRecordToInsert = new WebhookSubscriptionVerification
            {
                SubscriptionId = subscription.Id,
                Status = WebhookSubscriptionVerificationStatus.Pending,
                CreatedAt =DateTimeOffset.UtcNow,
                ChallengeToken = _secretKeyGenerator.GenerateKey(secretKeySize: 64)
            };

            await _repositoryContext.WebhookSubscriptionVerifications.AddAsync(signatureVerificationRecordToInsert, ct);

            await _repositoryContext.SaveChangesAsync(ct);

            var queueResult = await _applicationPublisher.QueueSignatureVerificationEvent(new SubscriptionVerificationRaised(signatureVerificationRecordToInsert.Id), ct);

            _logger.Information("Signature verification record created successfully for subscription - {0}, Queue Result: {1}", SubscriptionId, queueResult);

            return GenericResponse<string>.Success("Operation Successful.", "Subscription callback URL verification initiated successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while verifying subscription callback URL for subscription - {0}", SubscriptionId);
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred processing request.", HttpStatusCode.InternalServerError);
        }

    }

    public async Task<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>> GetSubscriptionVerifications(Guid SubscriptionId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetSubscriptionVerifications));

        try
        {
            _logger.Information("Fetching subscription verification records for - {0}", SubscriptionId);

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
