using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;

namespace Webhook.Web.Handlers.Delivery;

public class GetDeliveryDetailsHandler
{
    private readonly HttpClient _httpClient;
    public GetDeliveryDetailsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, WebhookDeliveryDetailsDto? deliveryDetails)> HandleAsync(Guid deliveryId)
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync($"api/WebhookDeliveries/{deliveryId}");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<WebhookDeliveryDetailsDto> responseBody = JsonSerializer.Deserialize<GenericResponse<WebhookDeliveryDetailsDto>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
        }
    }
}
