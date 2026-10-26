using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Verifies one-time passwords and issues the operation tokens that authorise sensitive operations.
/// </summary>
/// <remarks>
/// OTPs are created by <see cref="IAuthenticationService.RequestOtpAsync"/>. Verifying one
/// returns a data-protected operation token which the client sends in the
/// <c>X-Operation-Token</c> header of the operation the OTP was requested for.
/// </remarks>
public interface IOtpService
{
    /// <summary>
    /// Verifies an OTP and issues an operation token.
    /// </summary>
    /// <remarks>
    /// Only the user's most recent OTP that is unexpired, unrevoked, unconsumed and not yet
    /// verified is checked. On success it is marked verified and an
    /// <see cref="Entities.OtpOperationToken"/> with the same purpose is stored (hashed), with
    /// a lifetime from <see cref="Entities.ConfigurationModels.TokenValidationConfiguration.OtpOperationTokenExpiresAFterInSceonds"/>.
    /// </remarks>
    /// <param name="otpVerificationRequest">The email address and OTP.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="OtpVerificationDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — OTP verified; the operation token is returned.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — unknown user, no usable OTP, wrong OTP, or the token could not be created.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<OtpVerificationDto>> ValidateOtpAsync(OtpVerificationRequestDto otpVerificationRequest, CancellationToken ct = default);
    /// <summary>
    /// Revokes all of a user's unexpired, unconsumed OTPs.
    /// </summary>
    /// <param name="userId">The ID of the user whose OTPs to revoke.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A successful <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — one or more OTPs revoked.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NoContent"/> — the user had no OTPs to revoke.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> RevokeUserOtpAsync(Guid userId, CancellationToken ct = default);
}
