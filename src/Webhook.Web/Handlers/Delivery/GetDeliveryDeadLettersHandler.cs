using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;

namespace Webhook.Web.Handlers.Delivery;

public class GetDeliveryDeadLettersHandler
{
    private readonly HttpClient _httpClient;
	public GetDeliveryDeadLettersHandler(IHttpClientFactory httpClientFactory)
	{
		_httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
	}

	public async Task<(ApiResponseDto responseDetails, IReadOnlyList<DeadLetterQueueDto> responseItems)> HandleAsync(Guid deliveryId)
	{
		try
		{
			var httpResponse = await _httpClient.GetAsync($"api/WebhookDelivery/{deliveryId}/deadLetters");
			string responseContent = await httpResponse.Content.ReadAsStringAsync();

			GenericResponse<IReadOnlyList<DeadLetterQueueDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
				throw new InvalidOperationException("Http Response could not be deserialized.");

			return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData ?? []);
		}
		catch (Exception ex) 
		{
			return (new ApiResponseDto(false, ex.Message), []);
		}
	}
}
