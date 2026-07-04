using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookEventService
{
    Task<GenericResponse<string>> CreateEventAsync(CreateWebhookEventDto createWebhookEvent, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventsAsync(GetWebhookEventParameters parameters, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<WebhookEventDto>>> GetWebhookEventAsync(Guid correlationId, CancellationToken ct = default);
}
