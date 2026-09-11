using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace Webhook.Web.Handlers.ServiceClient;

public class GetClientDetailsByClientIdHandler
{
    private readonly HttpClient _httpClient;
    public GetClientDetailsByClientIdHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, WebhookServiceClientDto? serviceClient)> HandleAsync(string clientId)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookServiceClients/{clientId}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<WebhookServiceClientDto> responseBody = JsonSerializer.Deserialize<GenericResponse<WebhookServiceClientDto>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("HttpResponsecould not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
        }
    }
}