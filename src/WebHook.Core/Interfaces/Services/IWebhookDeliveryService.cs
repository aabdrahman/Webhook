using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.RequestParameter;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Reads webhook deliveries for the portal.
/// </summary>
public interface IWebhookDeliveryService
{
    /// <summary>
    /// Gets a page of deliveries, newest first.
    /// </summary>
    /// <remarks>
    /// Admins see all deliveries; other users see only deliveries to subscriptions they created.
    /// Deliveries created from the start of <see cref="WebhookDeliveryQueryRequestParameter.StartDate"/>
    /// up to the start of <see cref="WebhookDeliveryQueryRequestParameter.EndDate"/> (midnight) are included.
    /// The status and subscription filters are applied when provided.
    /// </remarks>
    /// <param name="webhookDeliveryQueryRequest">Paging and filter parameters.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="PagedListItem{WebhookDeliveryDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the page was read; it may be empty.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — a non-admin caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<PagedListItem<WebhookDeliveryDto>>> GetDeliveriesAsync(WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequest, CancellationToken ct = default);
    /// <summary>
    /// Gets the full details of a delivery, including its payload and attempt counts.
    /// </summary>
    /// <remarks>
    /// The caller's ownership of the delivery is not checked.
    /// </remarks>
    /// <param name="deliveryId">The delivery ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="WebhookDeliveryDetailsDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the delivery was found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no delivery with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<WebhookDeliveryDetailsDto>> GetDeliveryDetailsAsync(Guid deliveryId, CancellationToken ct = default);
}
