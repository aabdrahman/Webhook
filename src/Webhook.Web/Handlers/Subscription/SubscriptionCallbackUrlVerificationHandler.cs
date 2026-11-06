using System.Text;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.Subscription;


public class SubscriptionCallbackUrlVerificationHandler
{
    private readonly HttpClient _httpClient;
    public SubscriptionCallbackUrlVerificationHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid SubscriptionId)
    {
        try
        {
            var requestContent = new StringContent(SubscriptionId.ToString(), encoding: Encoding.UTF8, "application/json");
            var httpResponse = await _httpClient.PutAsync($"api/WebhookSubscription/{SubscriptionId}/verifications/verify", requestContent);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Api response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);

        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}
