using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.Authentication;

public class RegisterUserHandler
{
    private readonly HttpClient _httpClient;

    public RegisterUserHandler(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameOpen);
    }

    public async Task<ApiResponseDto> HandleAsync(CreateUserDto createUser)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("api/Users/register", createUser);
            string responseContent = await httpResponse.Content.ReadAsStringAsync();
            GenericResponse<string> genericResponse = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new Exception("Failed to deserialize the response.");

            return new ApiResponseDto(genericResponse.IsSuccessful, genericResponse.ResponseMessage);
        }
        catch (HttpRequestException httpEx)
        {
            return new ApiResponseDto(false, $"HTTP Request Error: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
