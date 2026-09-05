using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.Authentication;

public class RequestOtpHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorageService;

    public RequestOtpHandler(IHttpClientFactory httpClientFactory, ILocalStorageService localStorageService)
    {
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameOpen);
        _localStorageService = localStorageService;
    }

    public async Task<ApiResponseDto> HandleAsync(RequestOtpDto requestOtp)
    {
        try
        {
            var httpResponse = await _httpClient.PostAsJsonAsync("api/Authentication/request-otp", requestOtp);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize the response body.");

            if (responseBody.IsSuccessful)
            {
                // Store the response data in local storage
                await _localStorageService.SetItemAsync(ApplicationClientHelper.OtpRequestedUser, responseBody.ResponseData);
            }

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);

        }
        catch(HttpRequestException httpEx)
        {
            return new ApiResponseDto(false, $"HTTP Request Error: {httpEx.Message}");
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
