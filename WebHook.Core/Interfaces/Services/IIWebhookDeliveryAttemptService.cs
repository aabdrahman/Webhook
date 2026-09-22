using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;

namespace WebHook.Core.Interfaces.Services;

public interface IIWebhookDeliveryAttemptService
{
    Task<GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>> GetDeliveryAttemptsAsync(Guid deliveryId, CancellationToken ct = default);
}