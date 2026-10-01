using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;

namespace Webhook.Web.Handlers.ReportingAnalytics;

public class GetMostRecentEventsHandler
{
    private readonly HttpClient _httpClient;
    public GetMostRecentEventsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto, IReadOnlyList<TopFiveEventAnalyticsEntity>)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/ReportingAnalytics/most-recent-events");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<TopFiveEventAnalyticsEntity>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
            
        }
    }
}
