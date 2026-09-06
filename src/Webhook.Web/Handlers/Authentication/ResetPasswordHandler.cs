using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace Webhook.Web.Handlers.Authentication;

public class ResetPasswordHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorageService;

    public ResetPasswordHandler(ILocalStorageService localStorageService, IHttpClientFactory httpClientFactory)
    {
        _localStorageService = localStorageService;
        _httpClient = httpClientFactory.CreateClient(ApplicationClientHelper.ApplicationHttpClientNameOpen);
    }

    public async Task<ApiResponseDto> HandleAsync(ResetUserPasswordequestDto resetUserPasswordequest)
    {
        try
        {
            var operationToken = await _localStorageService.GetItemAsync<string>(ApplicationClientHelper.VerifiedOtpTokenKey);
            if (string.IsNullOrEmpty(operationToken))
            {
                return new ApiResponseDto(false, "An error occurred. Kindly request a new otp.");
            }

            _httpClient.DefaultRequestHeaders.Add(ApplicationClientHelper.OperationTokenHeaderKey, operationToken);
            var httpResponse = await _httpClient.PostAsJsonAsync("api/Authentication/reset-user-password", resetUserPasswordequest);

            string responseContent = await httpResponse.Content.ReadAsStringAsync();

            GenericResponse<string> responseBody = JsonSerializer.Deserialize<GenericResponse<string>>(responseContent, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true }) ?? 
                                                    throw new InvalidOperationException("An error occurred while deserilaizing response content.");

            if (responseBody.IsSuccessful)
            {
                await _localStorageService.RemoveItemAsync(ApplicationClientHelper.VerifiedOtpTokenKey);
            }

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
        }
        catch (Exception ex)
        {
            return new ApiResponseDto(false, ex.Message);
        }
    }
}
