using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Reads the HTTP attempts made for webhook deliveries.
/// </summary>
public interface IWebhookDeliveryAttemptService
{
    /// <summary>
    /// Lists the attempts made for a delivery, newest first.
    /// </summary>
    /// <remarks>
    /// The caller's ownership of the delivery is not checked.
    /// </remarks>
    /// <param name="deliveryId">The delivery ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{WebhookDeliveryAttemptDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more attempts found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no attempts have been made yet.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>> GetDeliveryAttemptsAsync(Guid deliveryId, CancellationToken ct = default);
}