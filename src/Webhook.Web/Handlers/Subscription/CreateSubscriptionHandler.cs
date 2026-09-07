using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;

namespace Webhook.Web.Handlers.Subscription;

public class CreateSubscriptionHandler
{
    private readonly HttpClient _httpClient;
    public CreateSubscriptionHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(CreateWebhookSubscriptionDto createWebhookSubscription)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("", createWebhookSubscription);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent,
                    new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }
                ) ?? throw new InvalidOperationException("Http response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}
