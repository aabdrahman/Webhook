using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;

namespace WebHook.Core.Interfaces.Services;

public interface ISubscriptionVerificationService
{
    Task<GenericResponse<string>> VerifySubscriptionCallbackUrl(Guid SubscriptionId, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>> GetSubscriptionVerifications(Guid SubscriptionId, CancellationToken ct = default);
}
