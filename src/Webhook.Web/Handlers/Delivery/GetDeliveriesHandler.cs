using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.RequestParameter;

namespace Webhook.Web.Handlers.Delivery;

public class GetDeliveriesHandler
{
    private readonly HttpClient _httpClient;
    public GetDeliveriesHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(GenericResponse<IReadOnlyList<WebhookDeliveryDto>> details, RequestMetaData? metaData)> HandleAsync(WebhookDeliveryQueryRequestParameter webhookDeliveryQueryRequest)
    {
        try
        {
            Dictionary<string, string?> queryParam = new Dictionary<string, string?>()
            {
                ["StartDate"] = webhookDeliveryQueryRequest.StartDate.ToString("yyyy-MM-dd"),
                ["EndDate"] = webhookDeliveryQueryRequest.EndDate.ToString("yyyy-MM-dd"),
                ["PageNumber"] = webhookDeliveryQueryRequest.PageNumber.ToString(),
                ["PageSize"] = webhookDeliveryQueryRequest.PageSize.ToString(),
                ["DeliveryStatus"] = webhookDeliveryQueryRequest.DeliveryStatus?.ToString(),
                ["SubscriptionId"] = webhookDeliveryQueryRequest.SubscriptionId?.ToString()
            };

            var queryString = QueryHelpers.AddQueryString("api/WebhookDeliveries", queryParam.Where(x => x.Value is not null).ToDictionary(x => x.Key, x => x.Value));

            var httpResponse = await _httpClient.GetAsync(queryString);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            string paginationMetaData = string.Empty;
            if (httpResponse.IsSuccessStatusCode)
            {
                var paginationHeader = httpResponse.Headers.GetValues("X-Pagination");
                paginationMetaData = paginationHeader.First().ToString();
            }

            GenericResponse<IReadOnlyList<WebhookDeliveryDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<WebhookDeliveryDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (responseBody, JsonSerializer.Deserialize<RequestMetaData>(paginationMetaData, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }));
        }
        catch (Exception ex)
        {
            return (GenericResponse<IReadOnlyList<WebhookDeliveryDto>>.Failure(null, ex.Message, System.Net.HttpStatusCode.InternalServerError), null);
        }
    }
}
