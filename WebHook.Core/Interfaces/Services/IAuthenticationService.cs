using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;

namespace WebHook.Core.Interfaces.Services;

/// <summary>
/// Authenticates portal users and manages their credentials, sessions and roles.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are HMAC-SHA256 JWTs whose audience is the caller's <c>Origin</c> header,
/// which must be one of the configured valid audiences. The JTI of each user's current
/// access token is cached under their normalised email; the API rejects any token whose
/// JTI does not match, so issuing a new token or clearing the cache entry revokes the old one.
/// </para>
/// <para>
/// Refresh tokens are random 32-byte values stored on the <see cref="Entities.User"/> with
/// an expiry. They rotate on every login and refresh.
/// </para>
/// </remarks>
public interface IAuthenticationService
{
    /// <summary>
    /// Verifies a user's password and issues an access and refresh token.
    /// </summary>
    /// <remarks>
    /// Failed password checks count towards ASP.NET Core Identity lockout. On success the
    /// user's refresh token, last login time and cached JTI are updated.
    /// </remarks>
    /// <param name="loginUserDetails">The user name or email address and password.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="TokenDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — signed in; tokens returned.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — missing or unrecognised origin, wrong password, locked out, or sign-in not allowed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no user with that user name or email.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the account has two-factor authentication enabled, which is not supported.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — the user record could not be updated, or an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<TokenDto>> LoginUserAsync(LoginUserDto loginUserDetails, CancellationToken ct = default);
    /// <summary>
    /// Changes a user's password after verifying their current one, and clears their refresh token.
    /// </summary>
    /// <param name="changePasswordRequest">
    /// The user's email address, current password and new password. Only email lookup is
    /// supported, despite the property name.
    /// </param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — password changed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — unknown user, wrong current password, or the new password fails policy.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ChangePasswordAsync(ChangePasswordDto changePasswordRequest, CancellationToken ct = default);
    /// <summary>
    /// Generates an OTP for a user, stores its hash and queues an email containing it.
    /// </summary>
    /// <remarks>
    /// The OTP length comes from <see cref="Entities.ConfigurationModels.OtpSettingsConfiguration"/> and its
    /// lifetime from <see cref="Entities.ConfigurationModels.TokenValidationConfiguration.OtpExpirationAfterInSeconds"/>.
    /// The email is queued, so a successful response does not guarantee delivery. Only
    /// <see cref="Constants.OtpPurpose.PasswordReset"/> has email subject and wording defined.
    /// </remarks>
    /// <param name="requestOtp">The user name or email address and the OTP's purpose.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> whose data is the user's normalised email. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — OTP stored and email queued.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — no user with that user name or email.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.FailedDependency"/> — the OTP could not be generated or hashed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> RequestOtpAsync(RequestOtpDto requestOtp, CancellationToken ct = default);
    /// <summary>
    /// Resets a user's password using the operation token from a verified
    /// <see cref="Constants.OtpPurpose.PasswordReset"/> OTP.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The operation token is read from the <c>X-Operation-Token</c> header (see
    /// <see cref="Helpers.IAuthenticatedUserDetails.operationToken"/>). It is unprotected,
    /// matched to an unexpired, unconsumed and unrevoked <see cref="Entities.OtpOperationToken"/>
    /// by JTI, checked against the stored hash, and must have been issued for password reset
    /// to the same user.
    /// </para>
    /// <para>
    /// On success the password is replaced, the account is reactivated and unlocked, the
    /// refresh token is cleared, and both the OTP and the operation token are marked consumed.
    /// </para>
    /// </remarks>
    /// <param name="resetUserPasswordequest">The new password and its confirmation.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — password reset.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — missing, tampered, expired, consumed or wrong-purpose token, or the new password fails policy.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ResetUserPasswordAsync(ResetUserPasswordequestDto resetUserPasswordequest, CancellationToken ct = default);
    /// <summary>
    /// Exchanges an access token and refresh token for a new pair.
    /// </summary>
    /// <remarks>
    /// The access token's audience must match the caller's origin and its JTI must match the
    /// cached JTI for the user. The refresh token must match the one stored for the user and be
    /// unexpired; a mismatch counts as a failed access attempt towards lockout.
    /// The access token's lifetime is also validated, so an expired access token cannot be refreshed.
    /// </remarks>
    /// <param name="tokenDetails">The current access token and refresh token.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="TokenDto"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — new tokens issued.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — any validation of the origin, tokens or user failed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — the access token failed validation (including expiry), or an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<TokenDto>> RefreshTokenAsync(TokenDto tokenDetails, CancellationToken ct = default);
    /// <summary>
    /// Replaces a user's roles with a single new role.
    /// </summary>
    /// <remarks>
    /// The user's cached JTI is cleared, so their current access token stops working and they
    /// must sign in again to receive a token with the new role.
    /// </remarks>
    /// <param name="changeUserRoleRequest">The user's email address and the role to assign.</param>
    /// <param name="ct">A token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="GenericResponse{T}"/> of <see cref="string"/>. Possible status codes:
    /// <list type="bullet">
    ///   <item><description><see cref="System.Net.HttpStatusCode.OK"/> — role reassigned.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.NotFound"/> — the user or the role does not exist.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.Conflict"/> — the user already has the role, or their existing roles could not be removed.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.BadRequest"/> — the new role could not be added.</description></item>
    ///   <item><description><see cref="System.Net.HttpStatusCode.InternalServerError"/> — an unexpected error occurred.</description></item>
    /// </list>
    /// </returns>
    Task<GenericResponse<string>> ChangeUserRoleAsync(ChangeUserRoleRequestDto changeUserRoleRequest, CancellationToken ct = default);
}
