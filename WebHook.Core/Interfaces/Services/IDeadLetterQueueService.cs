using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

namespace WebHook.Core.Interfaces.Services;

public interface IDeadLetterQueueService
{
    Task<GenericResponse<string>> RequestManualRetryAsync(RequestManualRetryDto requestManualRetry, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>> GetDeliveryDeadKetterAsync(Guid deliveryId, CancellationToken ct = default);
}
