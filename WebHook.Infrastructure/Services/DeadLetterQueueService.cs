using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class DeadLetterQueueService : IDeadLetterQueueService
{
    private readonly RepositoryContext _repositoryContext;
    private readonly DeadLetterManualRetryConfiguration _deadLetterManualRetryConfiguration;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;

    public DeadLetterQueueService(RepositoryContext repositoryContext, IOptionsMonitor<DeadLetterManualRetryConfiguration> optionsMonitor, IAuthenticatedUserDetails authenticatedUserDetails)
    {
        _repositoryContext = repositoryContext;
        _deadLetterManualRetryConfiguration = optionsMonitor.CurrentValue;
        _authenticatedUserDetails = authenticatedUserDetails;

        _logger = Log.ForContext(_className, nameof(DeadLetterQueueService));

    }

    private ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    public async Task<GenericResponse<string>> RequestManualRetryAsync(RequestManualRetryDto requestManualRetry, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RequestManualRetryAsync));

        try
        {
            _logger.Information("Requesting manual retry for the dead letter queue details >>>> {0}", requestManualRetry);

            WebhookDeadLetterQueue? deadLeterToRetry = await _repositoryContext.WebhookDeadLetterQueues.Include(x => x.webhookDelivery).FirstOrDefaultAsync(x => x.Id == requestManualRetry.DeadLetterId, ct);

            if (deadLeterToRetry is null)
            {
                _logger.Warning("The dead Letter with details to retry does not exist. Dead Letter Id: {0}", requestManualRetry.DeadLetterId);
                return GenericResponse<string>.Failure("Operation Faield.", "Dead Letter with Id does not exist.", HttpStatusCode.NotFound);
            }

            if (deadLeterToRetry.RetriedAt.HasValue)
            {
                _logger.Warning("Invalid Dead Letter queue to retry. Dead Letter with Id: {0} has already been retried at: {1} by: {2}.", deadLeterToRetry.Id, deadLeterToRetry.RetriedAt.Value, deadLeterToRetry.RetriedBy);
                return GenericResponse<string>.Failure("Operation Failed.", "Dead Letter queue already retried.", HttpStatusCode.Conflict);
            }

            if (deadLeterToRetry.webhookDelivery.DeliveryStatus != WebhookDeliveryStatus.DeadLetter)
            {
                _logger.Warning("The linked delivery to the dead letter queue record: {0} has an invalid sttaus: {1}.", deadLeterToRetry.Id, deadLeterToRetry.webhookDelivery.DeliveryStatus.ToString());
                return GenericResponse<string>.Failure("Operation Failed.", $"Could not proceed. Delivery Status: {deadLeterToRetry.webhookDelivery.DeliveryStatus.ToString()}", HttpStatusCode.BadRequest);
            }

            int currentDeliveryCycle = deadLeterToRetry.webhookDelivery.RetryCycle;

            if (deadLeterToRetry.webhookDelivery.RetryCycle >= _deadLetterManualRetryConfiguration.MaximumRetryCycle)
            {
                _logger.Warning("Delivery already exceeds the maximum retry cycle. Current Retry Cycle: {0}, Maximum Retry Cycle: {1}", currentDeliveryCycle, _deadLetterManualRetryConfiguration.MaximumRetryCycle);
                return GenericResponse<string>.Failure("Operation Failed.", "Retry cycle already exceeded for the delivery.", HttpStatusCode.UnprocessableEntity);
            }

            deadLeterToRetry.RetryJustification = requestManualRetry.RetryJustification;
            deadLeterToRetry.RetriedAt = DateTimeOffset.UtcNow;
            deadLeterToRetry.RetriedBy = _authenticatedUserDetails.userId;

            deadLeterToRetry.webhookDelivery.RetryCycle++;
            deadLeterToRetry.webhookDelivery.NextRetryAt = null;
            deadLeterToRetry.webhookDelivery.LockedBy = null;
            deadLeterToRetry.webhookDelivery.LockedUntil = null;
            deadLeterToRetry.webhookDelivery.DeliveryStatus = WebhookDeliveryStatus.Pending;

            try
            {
                await _repositoryContext.SaveChangesAsync(ct);

                _logger.Information("Manual retry requested successfully for dead letter queue item - {0}", requestManualRetry);

                return GenericResponse<string>.Success("Operation Successful.", "Manual retry requested successfully for dead letter queue item.", HttpStatusCode.OK);
            }
            catch (DbUpdateException ex)
            {
                _logger.Error(ex, "An error occurred while updating the dead letter details.");
                return GenericResponse<string>.Failure("Operation Failed.", "Could not perform operation. Kindly retry.", HttpStatusCode.InternalServerError);
            }

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while performing manual request operation.");
            return GenericResponse<string>.Failure("Operation Failed", "An error occurred.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorDescription = ex.InnerException?.Message ?? "", ErrorMessage = ex.Message });
        }
    }

    public async Task<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>> GetDeliveryDeadKetterAsync(Guid deliveryId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(GetDeliveryDeadKetterAsync));

        try
        {
            _logger.Information("Fetching dead letter queue record for delivery - {0}", deliveryId);

            IReadOnlyList<DeadLetterQueueDto> deadLetterItems = await _repositoryContext.WebhookDeadLetterQueues.Where(x => x.WebhookDeliveryId == deliveryId).Select(x => new DeadLetterQueueDto(x.Id, x.CreatedAt, x.Reason, x.RetriedAt, x.RetryJustification, x.RetriedBy)).ToListAsync(ct);

            return deadLetterItems.Any() ?
                GenericResponse<IReadOnlyList<DeadLetterQueueDto>>.Success(deadLetterItems, "Dead letter queues fetched successfully", HttpStatusCode.OK) :
                GenericResponse<IReadOnlyList<DeadLetterQueueDto>>.Failure(null, "Dead Letter queue items does not exist for teh delivery.", HttpStatusCode.NotFound);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while fetching dead letter queue items");
            return GenericResponse<IReadOnlyList<DeadLetterQueueDto>>.Failure(null, "An error occurred while fetching dead letter queue items.", HttpStatusCode.InternalServerError, new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });

        }

    }
}
