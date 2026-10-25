using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Manages webhook subscriptions: callback URLs registered by users to receive event types.
/// </summary>
/// <remarks>
/// Each subscription has its own signing secret, generated on creation and rotated on
/// reactivation. Users can act only on subscriptions they created; admins can act on any.
/// Deleting a subscription is a soft delete.
/// </remarks>
public interface IWebhookSubscriptionService
{
    /// <summary>
    /// Creates a subscription for the calling user to one or more event types.
    /// </summary>
    /// <remarks>
    /// Every event type must exist and be active, or nothing is created. The contact email
    /// defaults to the caller's email address.
    /// </remarks>
    /// <param name="createWebhookSubscription">The name, callback URL, event types and optional contact email.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.Created"/> — the subscription was created.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — an event type does not exist, or the caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the calling user no longer exists or is inactive.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> CreateWebhookSubscriptionAsync(CreateWebhookSubscriptionDto createWebhookSubscription, CancellationToken ct = default);
    /// <summary>
    /// Deactivates (soft-deletes) a subscription so it stops receiving deliveries.
    /// </summary>
    /// <param name="webhookSubscriptionId">The subscription ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the subscription was deactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no active subscription with that ID that the caller may manage.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> DeleteWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default);
    /// <summary>
    /// Lists all users' subscriptions. Intended for admins. Signing secrets are not included.
    /// </summary>
    /// <param name="includeDeactivated">Whether to include deactivated subscriptions.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{WebhookSubscriptionDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more subscriptions found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — there are no matching subscriptions.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>> GetAllWebhookSubscriptionAsync(bool includeDeactivated = false, CancellationToken ct = default);
    /// <summary>
    /// Gets a subscription by ID, whether active or not, including its decrypted signing secret.
    /// </summary>
    /// <param name="webhookSubscriptionId">The subscription ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="WebhookSubscriptionDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the subscription was found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no subscription with that ID that the caller may see.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<WebhookSubscriptionDto>> GetWebhookSubscriptionByIdAsync(Guid webhookSubscriptionId, CancellationToken ct = default);
    /// <summary>
    /// Reactivates a deactivated subscription and rotates its signing secret.
    /// </summary>
    /// <param name="webhookSubscriptionId">The subscription ID.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — the subscription was reactivated.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no subscription with that ID that the caller may manage.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the subscription is already active.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ActivateWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default);
    /// <summary>
    /// Lists the calling user's subscriptions. Signing secrets are not included.
    /// </summary>
    /// <param name="includeDeactivated">Whether to include deactivated subscriptions.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="IReadOnlyList{WebhookSubscriptionDto}"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more subscriptions found.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Forbidden"/> — the caller's user ID claim is invalid.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the caller has no matching subscriptions (the data is an empty list).</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred; details captured in <see cref="ErrorDetail"/>.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>> GetUserSubscriptionsAsync(bool includeDeactivated = false, CancellationToken ct = default);
}
