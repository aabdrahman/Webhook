using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.Subscription;

public class ReactivateSubscriptionHandler
{
    private readonly HttpClient _httpClient;
    public ReactivateSubscriptionHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid subscriptionId)
    {
        try
        {
            var httpResponse = await _httpClient.PutAsJsonAsync($"api/WebhookSubscription/{subscriptionId}", subscriptionId);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent,
                    new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }
                ) ?? throw new InvalidOperationException("Http response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            throw;
        }
    }
}
