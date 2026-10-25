using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Onboards and manages the internal services allowed to publish events.
/// </summary>
/// <remarks>
/// Client IDs are compared in lower case. Client keys are returned only when issued;
/// only their hash is stored.
/// </remarks>
public interface IWebhookServiceClientService
{
    /// <summary>
    /// Onboards a service client, assigns its allowed event types and issues its client key.
    /// </summary>
    /// <remarks>
    /// Every allowed event type must exist and be active. If the calling admin's account no
    /// longer exists or is inactive, their cached token is cleared and the request is refused.
    /// </remarks>
    /// <param name="createServiceClient">The service name, client ID, contact email and allowed event types.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="ServiceClientOnboardingResponse"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.Created"/> — the client was onboarded; the response carries the raw client key.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the client ID is already in use.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — one or more event types do not exist or are inactive.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the calling admin's account is no longer active.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — a client key could not be generated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<ServiceClientOnboardingResponse>> OnboardNewServiceClientAsync(CreateServiceClientDto createServiceClient, CancellationToken ct = default);
    /// <summary>
    /// Issues a new client key for an active service client. The previous key stops working immediately.
    /// </summary>
    /// <param name="requestNewClientKey">The client ID of the service.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="ServiceClientOnboardingResponse"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the key was rotated; the response carries the new raw key.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no active client with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<ServiceClientOnboardingResponse>> RequestNewClientKeyAsync(RequestNewClientKeyDto requestNewClientKey, CancellationToken ct = default);
    /// <summary>
    /// Deactivates a service client and removes all of its event type assignments.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.NoContent"/> — the client was deactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the calling admin's account is no longer active.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no active client with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> DeactivateClientAsync(string clientId, CancellationToken ct = default);
    /// <summary>
    /// Reactivates a deactivated service client.
    /// </summary>
    /// <remarks>
    /// Event type assignments removed at deactivation are not restored; reassign them with
    /// <see cref="IWebhookServiceClientCatalogService.SubscribeToCatalogAsync"/>.
    /// </remarks>
    /// <param name="clientId">The client ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the client was reactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no client with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the client is already active.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ReactivateClientAsync(string clientId, CancellationToken ct = default);
    /// <summary>
    /// Lists service clients.
    /// </summary>
    /// <param name="includeDeactivated">Whether to include deactivated clients.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{WebhookServiceClientDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more clients found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — there are no matching clients.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>> GetAllClientsAsync(bool includeDeactivated = false, CancellationToken ct = default);
    /// <summary>
    /// Gets a service client by client ID, whether active or not.
    /// </summary>
    /// <param name="clientId">The client ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="WebhookServiceClientDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the client was found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no client with that ID.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<WebhookServiceClientDto>> GetByClientIdAsync(string clientId, CancellationToken ct = default);
}
