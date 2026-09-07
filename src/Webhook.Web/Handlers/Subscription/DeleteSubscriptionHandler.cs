using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.Subscription;

public class DeleteSubscriptionHandler
{
    private readonly HttpClient _httpClient;
    public DeleteSubscriptionHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid subscriptionId, bool isDeactivate = true)
    {
        try
        {
            var httpResponse = await _httpClient.DeleteAsync($"pi/WebhookSubscription/{subscriptionId}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent,
                    new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }
                ) ?? throw new InvalidOperationException("Response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}
