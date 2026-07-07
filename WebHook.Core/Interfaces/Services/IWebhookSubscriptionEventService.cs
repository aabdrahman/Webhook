using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookSubscriptionEventService
{
    Task<GenericResponse<string>> SubscribeToEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default);
    Task<GenericResponse<string>> UnsubscribeFromEventAsync(Guid subscriptionId, string eventName, CancellationToken cancellationToken = default);
    Task<GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>> GetSubscribedEventsAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
}
