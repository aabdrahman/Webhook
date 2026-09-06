using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.User;

public class ChangePasswordHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorageService;

    public ChangePasswordHandler(IHttpClientFactory httpClientFactory, ILocalStorageService localStorageService)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameSecure);
        _localStorageService = localStorageService;
    }

    public async Task<ApiResponseDto> HandleAsync(ChangePasswordDto changePasswordRequest)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("api/Authentication/change-password", changePasswordRequest);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ??
                                        throw new InvalidOperationException("An error occurred while deserializing http response.");

            if (responseBody.IsSuccessful)
            {
                await _localStorageService.RemoveItemAsync(ApplicationClientHelper.StorageKey);
            }

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
