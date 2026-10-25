using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Reads dead-letter history and lets admins retry dead-lettered deliveries.
/// </summary>
public interface IDeadLetterQueueService
{
    /// <summary>
    /// Puts a dead-lettered delivery back into the delivery pipeline for a new retry cycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The dead-letter entry is stamped with the justification, time and requesting admin,
    /// and can only be retried once. The delivery's <see cref="Entities.WebhookDelivery.RetryCycle"/>
    /// is incremented, its lock and next retry time are cleared, and its status is set to
    /// <see cref="Constants.WebhookDeliveryStatus.Pending"/> so the delivery processor picks it up.
    /// </para>
    /// <para>
    /// A delivery cannot be retried once its retry cycle reaches
    /// <see cref="Entities.ConfigurationModels.DeadLetterManualRetryConfiguration.MaximumRetryCycle"/>.
    /// </para>
    /// </remarks>
    /// <param name="requestManualRetry">The dead-letter entry to retry and the justification.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the delivery was requeued.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no dead-letter entry with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the entry has already been retried.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — the delivery is no longer in <see cref="Constants.WebhookDeliveryStatus.DeadLetter"/> status.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.UnprocessableEntity"/> — the maximum retry cycle has been reached.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — the update failed, or an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> RequestManualRetryAsync(RequestManualRetryDto requestManualRetry, CancellationToken ct = default);
    /// <summary>
    /// Lists the dead-letter entries recorded for a delivery.
    /// </summary>
    /// <param name="deliveryId">The delivery ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{DeadLetterQueueDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more entries found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the delivery has never been dead-lettered.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>> GetDeliveryDeadLetterAsync(Guid deliveryId, CancellationToken ct = default);
}
