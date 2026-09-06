using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;

namespace Webhook.Web.Handlers.Subscription;

public class GetSubscriptionsHandler
{
    private readonly HttpClient _httpClient;

    public GetSubscriptionsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<WebhookSubscriptionDto> subscriptions)> HandleAsync(bool isAdmin = false)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync(isAdmin ? "api/WebhookSubscription" : "api/WebhookSubscription/get-user-subscriptions");

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<WebhookSubscriptionDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Api response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}
