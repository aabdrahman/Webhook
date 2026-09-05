using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.Authentication;

public class RefreshTokenHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILocalStorageService _localStorageService;

    public RefreshTokenHandler(IHttpClientFactory httpClientFactory, ILocalStorageService localStorageService)
    {
        _httpClientFactory = httpClientFactory;
        _localStorageService = localStorageService;
    }

    public async Task<string?> RefreshTokenAsync(TokenDto sessionToken)
    {
        var httpClient = _httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameOpen);
       
        var response = await httpClient.PostAsJsonAsync("api/Authentication/refresh", sessionToken);

        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var responseContent = await response.Content.ReadAsStringAsync();
        GenericResponse<TokenDto>? jsonResponse = JsonSerializer.Deserialize<GenericResponse<TokenDto>>(responseContent);

        if(jsonResponse is null || !jsonResponse.IsSuccessful || jsonResponse.ResponseData is null)
        {
            return string.Empty;
        }

        await _localStorageService.SetItemAsync<TokenDto>(ApplicationClientHelper.StorageKey, jsonResponse.ResponseData);

        return jsonResponse.ResponseData.accessToken;

    }
}
