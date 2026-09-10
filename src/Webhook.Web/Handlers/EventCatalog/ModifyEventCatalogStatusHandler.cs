using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.EventCatalog;

public class ModifyEventCatalogStatusHandler
{
    private readonly HttpClient _httpClient;
    public ModifyEventCatalogStatusHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid eventCatalogId, bool isDeactivate = true)
    {
        try
        {
            var httpResponse = await _httpClient.PutAsJsonAsync($"/api/WebhookEventCatalog/{eventCatalogId}?isDeactivate={isDeactivate}", eventCatalogId);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();
            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}
