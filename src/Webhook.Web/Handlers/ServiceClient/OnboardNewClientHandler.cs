using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace Webhook.Web.Handlers.ServiceClient;

public class OnboardNewClientHandler
{
    private readonly HttpClient _httpClient;
    public OnboardNewClientHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, ServiceClientOnboardingResponse? responseDetails)> HandleAsync(CreateServiceClientDto createServiceClient)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("api/WebhookServiceClients", createServiceClient);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<ServiceClientOnboardingResponse> responseBody = JsonSerializer.Deserialize<GenericResponse<ServiceClientOnboardingResponse>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Respons ecould not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
        }
    }
}