using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;

namespace Webhook.Web.Handlers.Delivery;

public class GetDeliveryAttemptsHandler
{
    private readonly HttpClient _httpClient;
    public GetDeliveryAttemptsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, IReadOnlyList<WebhookDeliveryAttemptDto> deliveryAttempts)> HandleAsync(Guid deliveryId)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookDeliveryAttempts/{deliveryId}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<WebhookDeliveryAttemptDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), []);

        }
    }
}