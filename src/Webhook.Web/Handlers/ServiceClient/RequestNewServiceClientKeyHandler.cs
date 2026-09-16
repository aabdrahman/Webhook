using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace Webhook.Web.Handlers.ServiceClient;

public class RequestNewServiceClientKeyHandler
{
    private readonly HttpClient _httpClient;
    public RequestNewServiceClientKeyHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, ServiceClientOnboardingResponse? responseDetails)> HandleAsync(RequestNewClientKeyDto requestNewClientKey)
    {
        try
        {
            var httpResponse = await _httpClient.PutAsJsonAsync("api/WebhookServiceClients/request-new-key", requestNewClientKey);
            string responseCOntent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<ServiceClientOnboardingResponse> responseBody = JsonSerializer.Deserialize<GenericResponse<ServiceClientOnboardingResponse>>(responseCOntent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Respons ecould not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
        }
    }
}