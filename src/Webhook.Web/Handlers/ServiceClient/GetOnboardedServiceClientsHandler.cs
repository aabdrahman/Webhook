using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace Webhook.Web.Handlers.ServiceClient;

public class GetOnboardedServiceClientsHandler
{
    private readonly HttpClient _httpClient;
    public GetOnboardedServiceClientsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<WebhookServiceClientDto> webhookServiceClients)> HandleAsync(bool includeDeactivated = false)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookServiceClients?includeDeactivated={includeDeactivated}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<WebhookServiceClientDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody?.ResponseData ?? []);

        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}
