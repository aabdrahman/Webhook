using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing one-time password (OTP) operations.
/// </summary>
/// <remarks>
/// These endpoints handle the verification and revocation stages of the OTP
/// lifecycle. OTP generation is initiated via
/// <c>POST /api/Authentication/request-otp</c>.
///
/// The typical OTP flow is:
/// <list type="number">
///   <item><description>Client calls <c>POST /api/Authentication/request-otp</c> — OTP generated and emailed.</description></item>
///   <item><description>User submits the received code to <c>POST /api/OtpOperation/validate-otp</c> — OTP verified.</description></item>
///   <item><description>On success the caller may proceed with the gated operation (e.g. password reset).</description></item>
/// </list>
///
/// Administrators may call <c>DELETE /api/OtpOperation/revoke-otp/{userId}</c>
/// to immediately invalidate any active OTP for a given user.
/// </remarks>
[Route("api/[controller]")]
[ApiController]
public class OtpOperationController : ControllerBase
{
    private readonly IOtpService _otpService;

    /// <summary>
    /// Initializes a new instance of the <see cref="OtpOperationController"/> class.
    /// </summary>
    /// <param name="otpService">
    /// The service responsible for OTP validation and revocation.
    /// </param>
    public OtpOperationController(IOtpService otpService)
    {
        _otpService = otpService;
        _logger = Log.ForContext(_className, nameof(OtpOperationController));
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger;

    /// <summary>
    /// Validates a one-time password submitted by the user.
    /// </summary>
    /// <remarks>
    /// Verifies that the submitted OTP matches the one issued for the account,
    /// has not expired, and has not already been used or revoked.
    ///
    /// An OTP is valid for <b>10 minutes</b> from the time it was generated.
    /// Once validated, the OTP is consumed and cannot be used again. If the OTP
    /// has expired or is invalid, the caller must request a new one via
    /// <c>POST /api/Authentication/request-otp</c>.
    ///
    /// On successful validation the caller may proceed with the operation the
    /// OTP was requested for (e.g. completing a password reset).
    /// </remarks>
    /// <param name="otpVerificationRequest">
    /// Contains the user identifier and the OTP code submitted by the user.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response if the OTP is valid and has not expired, or a
    /// descriptive error response if the OTP is invalid, expired, or already used.
    /// </returns>
    /// <response code="200">OTP validated successfully. The caller may proceed with the gated operation.</response>
    /// <response code="400">The OTP is invalid, has already been used, or does not match the issued code.</response>
    /// <response code="404">No active OTP was found for the specified user.</response>
    /// <response code="410">The OTP has expired. A new OTP must be requested.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpPost("validate-otp")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [AllowAnonymous]
    [EnableRateLimiting("validate-otp-limit")]
    public async Task<IActionResult> ValidateOtp([FromBody] OtpVerificationRequestDto otpVerificationRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ValidateOtp));
        try
        {
            var result = await _otpService.ValidateOtpAsync(otpVerificationRequest, ct);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }

    /// <summary>
    /// Immediately revokes any active one-time password for the specified user.
    /// </summary>
    /// <remarks>
    /// This endpoint is intended for administrative use — for example when a user
    /// reports receiving an OTP they did not request, or when an administrator
    /// needs to invalidate a potentially compromised OTP before it expires naturally.
    ///
    /// Revoking an OTP does not affect the user's account password or access token.
    /// If the user still needs to complete an OTP-gated operation they must call
    /// <c>POST /api/Authentication/request-otp</c> to receive a new code.
    ///
    /// If no active OTP exists for the user this endpoint returns a 404 response.
    /// </remarks>
    /// <param name="userId">
    /// The unique identifier of the user whose active OTP should be revoked.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the OTP was revoked, or a descriptive error
    /// response if no active OTP was found for the user.
    /// </returns>
    /// <response code="200">OTP revoked successfully.</response>
    /// <response code="404">No active OTP was found for the specified user.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpDelete("revoke-otp/{userId:guid}")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> RevokeOtp(Guid userId, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RevokeOtp));
        try
        {
            var result = await _otpService.RevokeUserOtpAsync(userId, ct);
            return StatusCode((int)result.HttpStatusCode, result);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred invoking endpoint.");
            return StatusCode(500, GenericResponse<string>.Failure(null,
                "An error occurred invoking endpoint.",
                System.Net.HttpStatusCode.InternalServerError,
                new ErrorDetail
                {
                    ErrorMessage = ex.Message,
                    ErrorTitle = ex.GetType().Name,
                    ErrorDescription = ex.InnerException?.Message ?? ""
                }));
        }
    }
}