using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.Authentication;

public class LoginHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorageService;

    public LoginHandler(ILocalStorageService localStorageService, IHttpClientFactory httpClientFactory)
    {
        _localStorageService = localStorageService;
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameOpen);
    }

    public async Task<ApiResponseDto> HandleAsync(LoginUserDto loginUser, bool rememberMe = false)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("api/Authentication/login", loginUser);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();
            Console.WriteLine($" Status Code: {httpResponse.StatusCode}. Response Content: {responseContent}.");

            GenericResponse<TokenDto> genericResponse = JsonSerializer.Deserialize<GenericResponse<TokenDto>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new Exception("Failed to deserialize the response.");

            if(genericResponse.IsSuccessful && genericResponse.ResponseData is not null)
            {
                await _localStorageService.SetItemAsync<TokenDto>(ApplicationClientHelper.StorageKey, genericResponse.ResponseData);

                if(rememberMe)
                {
                    await _localStorageService.SetItemAsync(ApplicationClientHelper.RememberMeKey, true);
                }
                else
                {
                    await _localStorageService.RemoveItemAsync(ApplicationClientHelper.RememberMeKey);
                }
            }

            return new ApiResponseDto(genericResponse.IsSuccessful, genericResponse.ResponseMessage);
        }
        catch(HttpRequestException httpEx)
        {
            return new ApiResponseDto(false, $"Status Code: {((int)httpEx.StatusCode.GetValueOrDefault()).ToString()}.HTTP Request Error: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
