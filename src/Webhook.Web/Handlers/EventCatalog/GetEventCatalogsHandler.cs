using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;

namespace Webhook.Web.Handlers.EventCatalog;

public class GetEventCatalogsHandler
{
    private readonly HttpClient _httpClient;

    public GetEventCatalogsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<EventCatalogDto> catalogs)> HandleAsync(bool includeDeactivated = false)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookEventCatalog?includeDeactivated={includeDeactivated}");

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<EventCatalogDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<EventCatalogDto>>>(responseContent,
                new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}
