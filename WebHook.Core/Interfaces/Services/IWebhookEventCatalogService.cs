using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookEventCatalogService
{
    Task<GenericResponse<string>> CreateNewEventCatalogAsync(CreateEventCatalogDto createEventCatalogDto, CancellationToken ct = default);
    Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default);
    Task<GenericResponse<EventCatalogDto>> GetEventCatlogByIdAsync(Guid EventCatlogId, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<EventCatalogDto>>> GetAllEventCatalogAsync(CancellationToken ct = default);
}
