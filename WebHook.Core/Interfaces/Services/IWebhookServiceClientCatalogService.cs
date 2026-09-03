using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookServiceClientCatalogService
{
    Task<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>> GetSubscribedCatalogsAsync(Guid serviceClientId, bool includeDeactivated = false, CancellationToken ct = default);
    Task<GenericResponse<string>> SubscribeToCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default);
    Task<GenericResponse<string>> UnSubscribeFromCatalogAsync(Guid serviceClientId, string catalogName, CancellationToken ct = default);
}
