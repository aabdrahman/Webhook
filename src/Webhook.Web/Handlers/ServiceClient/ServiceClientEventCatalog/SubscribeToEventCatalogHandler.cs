using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.ServiceClient.ServiceClientEventCatalog;

public class SubscribeToEventCatalogHandler
{
    private readonly HttpClient _httpClient;
    public SubscribeToEventCatalogHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid clientId, string eventToSubscribe)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync($"api/WebhookServiceClients/{clientId}/eventcatalogs?catalogName={eventToSubscribe}", eventToSubscribe);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) 
                ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}
