using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;

namespace WebHook.Core.Interfaces.Services;

public interface IOtpService
{
    Task<GenericResponse<OtpVerificationDto>> ValidateOtpAsync(OtpVerificationRequestDto otpVerificationRequest, CancellationToken ct = default);
    Task<GenericResponse<string>> RevokeUserOtpAsync(Guid userId, CancellationToken ct = default);
}
