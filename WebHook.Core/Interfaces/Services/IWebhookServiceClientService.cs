using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace WebHook.Core.Interfaces.Services;

public interface IWebhookServiceClientService
{
    Task<GenericResponse<ServiceClientOnboardingResponse>> OnboardNewServiceClientAsync(CreateServiceClientDto createServiceClient, CancellationToken ct = default);
    Task<GenericResponse<ServiceClientOnboardingResponse>> RequestNewClientKeyAsync(RequestNewClientKeyDto requestNewClientKey, CancellationToken ct = default);
    Task<GenericResponse<string>> DeactivateClientAsync(string clientId, CancellationToken ct = default);
    Task<GenericResponse<string>> ReactivateClientAsync(string clientId, CancellationToken ct = default);
    Task<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>> GetAllClientsAsync(bool includeDeactivated = false, CancellationToken ct = default);
    Task<GenericResponse<WebhookServiceClientDto>> GetByClientIdAsync(string clientId, CancellationToken ct = default);
}
