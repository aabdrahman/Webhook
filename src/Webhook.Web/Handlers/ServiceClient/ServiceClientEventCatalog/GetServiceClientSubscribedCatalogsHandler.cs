using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;

namespace Webhook.Web.Handlers.ServiceClient.ServiceClientEventCatalog;

public class GetServiceClientSubscribedCatalogsHandler
{
    private readonly HttpClient _httpClient;
    public GetServiceClientSubscribedCatalogsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<WebhookServiceClientCatalogDto> servceClientCatalogs)> HandleAsync(Guid serviceClientId)
    {
        try
        {
            //api/WebhookServiceClients/{serviceclientid}/eventcatalogs
            var httpResponse = await _httpClient.GetAsync($"api/WebhookServiceClients/{serviceClientId}/eventcatalogs");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Respons ecould not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody?.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
            
        }
    }
}
