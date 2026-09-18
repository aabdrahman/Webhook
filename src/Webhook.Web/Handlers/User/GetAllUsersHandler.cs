using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.User;

public class GetAllUsersHandler
{
    private readonly HttpClient _httpClient;
    public GetAllUsersHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
    }

    public async Task<(IReadOnlyList<UserDetailsDto> userDetails, ApiResponseDto response)> HandleAsync()
    {
        try
        {
            var httpResponse = await _httpClient.GetAsync("api/users");
            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<IReadOnlyList<UserDetailsDto>> responseBody = JsonSerializer.Deserialize<GenericResponse<IReadOnlyList<UserDetailsDto>>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                throw new InvalidOperationException("HttpResponse could not be deserialized.");

            return (responseBody.ResponseData ?? [], new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage));
        }
        catch (Exception ex)
        {
            return ([], new ApiResponseDto(false, ex.Message));
            
        }
    }
}
