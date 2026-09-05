using Blazored.LocalStorage;
using System.Net.Http.Json;
using System.Text.Json;
using Webhook.Web.Helper;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;

namespace Webhook.Web.Handlers.Authentication;

public class ValidateOtpHandler
{
    private readonly HttpClient _httpClient;
    private readonly ILocalStorageService _localStorageService;

    public ValidateOtpHandler(ILocalStorageService localStorageService, IHttpClientFactory httpClientFactory)
    {
        _localStorageService = localStorageService;
        _httpClient = httpClientFactory.CreateClient();
    }

    public async Task<ApiResponseDto> HandleAsync(string otp)
    {
        try
        {
            var requestedUserEmail = await _localStorageService.GetItemAsync<string>(ApplicationClientHelper.OtpRequestedUser);
            if(string.IsNullOrEmpty(requestedUserEmail))
            {
                return new ApiResponseDto(false, "An error occurred while validating the OTP. Kindly request new OTP.");
            }
            var httpResponse = await _httpClient.PostAsJsonAsync("api/OtpOperation/validate-otp", new OtpVerificationRequestDto { Otp = otp, EmailAddress = requestedUserEmail });
            string responseContent = await httpResponse.Content.ReadAsStringAsync();
            GenericResponse<OtpVerificationDto> responseBody = JsonSerializer.Deserialize<GenericResponse<OtpVerificationDto>>(responseContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("Failed to deserialize the response body.");

            if(responseBody.IsSuccessful && responseBody.ResponseData is not null)
            {
                await _localStorageService.SetItemAsync(ApplicationClientHelper.VerifiedOtpTokenKey, responseBody.ResponseData.SignedToken);
            }

            return new ApiResponseDto(responseBody.IsSuccessful, responseBody.ResponseMessage);
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
