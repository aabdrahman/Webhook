using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.RequestParameter;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookDeliveryService
{
    Task<GenericResponse<PagedListItem<WebhookDeliveryDto>>> GetDeliveriesAsync(WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequest, CancellationToken ct = default);
    Task<GenericResponse<WebhookDeliveryDetailsDto>> GetDeliveryDetailsAsync(Guid deliveryId, CancellationToken ct = default);
}
