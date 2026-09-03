using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Defines operations for managing webhook event catalogs.
/// Provides functionality to create, retrieve, and activate/deactivate event catalogs
/// which represent the list of subscribable webhook event types.
/// </summary>
public interface IWebhookEventCatalogService
{
    Task<GenericResponse<string>> CreateNewEventCatalogAsync(CreateEventCatalogDto createEventCatalogDto, CancellationToken ct = default);
    Task<GenericResponse<string>> EventCatalogActivationAsync(Guid EventCatalogId, bool isDeactivate = true, CancellationToken ct = default);
    Task<GenericResponse<EventCatalogDto>> GetEventCatalogByIdAsync(Guid EventCatalogId, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<EventCatalogDto>>> GetAllEventCatalogAsync(CancellationToken ct = default);
}
