using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;

namespace Webhook.Web.Handlers.Subscription;

public class AddEventToSubscriptionHandler
{
    private readonly HttpClient _httpClient;
    public AddEventToSubscriptionHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid subscriptionId, string eventToSubscribe)
    {
        try
        {
            var httpResponse = await _httpClient.PutAsJsonAsync($"api/WebhookSubscription/{subscriptionId}/events?eventName={eventToSubscribe}", eventToSubscribe);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Http Response could not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
            
        }
    }
}

public class UnsubscribeFromEventHandler
{
    private readonly HttpClient _httpClient;
    public UnsubscribeFromEventHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<ApiResponseDto> HandleAsync(Guid subscriptionId, string eventName)
    {
        try
        {
            var httpResponse = await _httpClient.DeleteAsync($"/api/WebhookSubscription/{subscriptionId}/events?eventName={eventName}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http response cound not be deserialized.");

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
