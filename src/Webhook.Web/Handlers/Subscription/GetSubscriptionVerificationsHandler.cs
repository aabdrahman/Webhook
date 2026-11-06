using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;

namespace Webhook.Web.Handlers.Subscription;

public class GetSubscriptionVerificationsHandler
{
    private readonly HttpClient _httpClient;
    public GetSubscriptionVerificationsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }
    public async Task<(ApiResponseDto response, IReadOnlyList<SubscriptionVerificationDto> verifications)> HandleAsync(Guid subscriptionId)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookSubscription/{subscriptionId}/verifications");

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<SubscriptionVerificationDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Api response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);
        }
    }
}
