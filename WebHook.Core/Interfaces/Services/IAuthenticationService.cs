using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace WebHook.Core.Interfaces.Services;

public interface IAuthenticationService
{
    Task<GenericResponse<TokenDto>> LoginUserAsync(LoginUserDto loginUserDetails, CancellationToken ct = default);
    Task<GenericResponse<string>> ChangePasswordAsync(ChangePasswordDto changePasswordRequest, CancellationToken ct = default);
    Task<GenericResponse<string>> RequestOtpAsync(RequestOtpDto requestOtp, CancellationToken ct = default);
}
