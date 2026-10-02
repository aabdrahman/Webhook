using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;

namespace Webhook.Web.Handlers.ReportingAnalytics;

public class GetEventCatalogAnalyticsHandler
{
    private readonly HttpClient _httpClient;
    public GetEventCatalogAnalyticsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<EventCatalogReportingAnalyticsEntity> catalogEntities)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/ReportingAnalytics/get-event-catalog-analytics\r\n");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<EventCatalogReportingAnalyticsEntity>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Respons ecould not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}
