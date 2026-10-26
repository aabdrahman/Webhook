using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Defines operations for managing the webhook events associated with a webhook subscription.
/// </summary>
/// <remarks>
/// This service provides functionality to:
/// <list type="bullet">
/// <item><description>Subscribe a webhook subscription to an event.</description></item>
/// <item><description>Remove an existing event subscription.</description></item>
/// <item><description>Retrieve all events currently subscribed to by a webhook subscription.</description></item>
/// </list>
/// </remarks>
public interface IWebhookSubscriptionEventService
{
    /// <summary>
    /// Subscribes a webhook subscription to the specified event.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the event to subscribe to.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> indicating whether the subscription was successful.
    /// </returns>
    Task<GenericResponse<string>> SubscribeToEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Removes an event subscription from a webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="eventName">
    /// The normalized name of the event to unsubscribe from.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> indicating whether the event was successfully removed.
    /// </returns>
    Task<GenericResponse<string>> UnsubscribeFromEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default);
    /// <summary>
    /// Retrieves all events currently subscribed to by a webhook subscription.
    /// </summary>
    /// <param name="subscriptionId">
    /// The unique identifier of the webhook subscription.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> containing the collection of subscribed events.
    /// </returns>
    Task<GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>> GetSubscribedEventsAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}
