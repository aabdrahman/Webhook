using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Manages which event types each service client is allowed to publish.
/// </summary>
/// <remarks>
/// Removing an assignment soft-deletes it, and assigning the same event type again restores it.
/// </remarks>
public interface IWebhookServiceClientCatalogService
{
    /// <summary>
    /// Lists the event types assigned to a service client.
    /// </summary>
    /// <param name="serviceClientId">The internal ID of the service client.</param>
    /// <param name="includeDeactivated">Whether to include removed assignments.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{WebhookServiceClientCatalogDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more assignments found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the client has no matching assignments.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>> GetSubscribedCatalogsAsync(Guid serviceClientId, bool includeDeactivated = false, CancellationToken ct = default);
    /// <summary>
    /// Allows a service client to publish an event type, restoring a previously removed assignment if there is one.
    /// </summary>
    /// <param name="serviceClientId">The internal ID of the service client.</param>
    /// <param name="catalogName">The event type name (case-insensitive).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the assignment was added or restored.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the client already has the event type.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the event type or the active client does not exist.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> SubscribeToCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default);
    /// <summary>
    /// Stops a service client from publishing an event type.
    /// </summary>
    /// <param name="serviceClientId">The internal ID of the service client.</param>
    /// <param name="catalogName">The event type name (case-insensitive).</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the assignment was removed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the client has no active assignment for the event type.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> UnSubscribeFromCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default);
}
