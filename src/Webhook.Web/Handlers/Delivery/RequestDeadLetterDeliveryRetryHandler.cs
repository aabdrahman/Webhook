using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

namespace Webhook.Web.Handlers.Delivery;

public class RequestDeadLetterDeliveryRetryHandler
{
    private readonly HttpClient _httpClient;
	public RequestDeadLetterDeliveryRetryHandler(IHttpClientFactory httpClientFactory)
	{
		_httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
	}

	public async Task<ApiResponseDto> HandleAsync(Guid deliveryId, RequestManualRetryDto requestManualRetry)
	{
		try
		{
			var httpResponse = await _httpClient.PostAsJsonAsync($"api/WebhookDelivery/{deliveryId}/deadLetters", requestManualRetry);
			string responseContent = await httpResponse.Content.ReadAsStringAsync();

			GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
				throw new InvalidOperationException("Http Respons ecould not be deserialized.");

			return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
		}
		catch (Exception ex)
		{
			return new ApiResponseDto(false, ex.Message);
			throw;
		}
	}
}
