using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.User;

public class GetUserDetailsHandler
{
    private readonly HttpClient _httpClient;
    public GetUserDetailsHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(ApiResponseDto response, UserDetailsDto? userDetails)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/Users/get-user-details");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<UserDetailsDto> responseBody = JsonSerializer.Deserialize<GenericResponse<UserDetailsDto>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("Http Response could not be deserialized.");

            return (new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage), responseBody.ResponseData);
        }
        catch (Exception ex)
        {
            return (new ApiResponseDto(false, ex.Message), null);
            throw;
        }
    }
}
