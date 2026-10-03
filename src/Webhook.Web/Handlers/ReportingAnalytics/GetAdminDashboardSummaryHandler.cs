using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities.CustomEntities;

namespace Webhook.Web.Handlers.ReportingAnalytics;

public class GetAdminDashboardSummaryHandler
{
    private readonly HttpClient _httpClient;
    public GetAdminDashboardSummaryHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, AdminDashboardSummaryReportingAnalyticsEntity? adminDahsboardSummary)> HandleAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("api/ReportingAnalytics/get-admin-dashboard-summary");

            var responseContent = await response.Content.ReadAsStringAsync();

            var apiResponse = JsonSerializer.Deserialize<GenericResponse<AdminDashboardSummaryReportingAnalyticsEntity>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (apiResponse == null)
            {
                throw new InvalidOperationException("Failed to deserialize API response.");
            }

            return (new ApiResponseDto(apiResponse.IsSuccessful, apiResponse.ResponseMessage), apiResponse.ResponseData);
            
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
        }
    }
}
