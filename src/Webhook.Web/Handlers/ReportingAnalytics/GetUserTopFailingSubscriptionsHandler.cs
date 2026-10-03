using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.ReportingAnalytics;

namespace Webhook.Web.Handlers.ReportingAnalytics;

public class GetUserTopFailingSubscriptionsHandler
{
    private readonly HttpClient _httpClient;
    public GetUserTopFailingSubscriptionsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<TopUserFailingSubscriptionDto> details)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/ReportingAnalytics/get-user-delivery-dashboard");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<TopUserFailingSubscriptionDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}

