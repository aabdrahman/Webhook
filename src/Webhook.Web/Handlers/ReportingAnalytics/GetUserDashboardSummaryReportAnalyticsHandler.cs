using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;

namespace Webhook.Web.Handlers.ReportingAnalytics;

public class GetUserDashboardSummaryReportAnalyticsHandler
{
    private readonly HttpClient _httpClient;
    public GetUserDashboardSummaryReportAnalyticsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, UserDashboardSummaryReportAnalyticsEntity? userDashboardSummary)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/ReportingAnalytics/get-user-dashboard-summary");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();
            GenericResponse<UserDashboardSummaryReportAnalyticsEntity> responseBody = JsonSerializer.Deserialize<GenericResponse<UserDashboardSummaryReportAnalyticsEntity>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
            
        }
    }
}
