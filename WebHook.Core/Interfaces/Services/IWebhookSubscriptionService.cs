using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookSubscriptionService
{
    Task<GenericResponse<string>> CreateWebhookSubscriptionAsync(CreateWebhookSubscriptionDto createWebhookSubscription, CancellationToken ct = default);
    Task<GenericResponse<string>> DeleteWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>> GetAllWebhookSubscriptionAsync(CancellationToken ct = default);
    Task<GenericResponse<WebhookSubscriptionDto>> GetWebhookSubscriptionByIdAsync(Guid webhookSubscriptionId, CancellationToken ct= default);
    Task<GenericResponse<string>> ActivateWebhookSubscriptionAsync(Guid webhookSubscriptionId, CancellationToken ct = default);
}
