using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;

namespace Webhook.Web.Handlers.Subscription;

public class GetSubscriptionDetailsHandler
{
    private readonly HttpClient _httpClient;

    public GetSubscriptionDetailsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response , WebhookSubscriptionDto? subscription)> HandleAsync(Guid subscriptionId)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookSubscription/{subscriptionId}");

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<WebhookSubscriptionDto> responseBody = JsonSerializer.Deserialize<GenericResponse<WebhookSubscriptionDto>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
            throw;
        }
    }
}
