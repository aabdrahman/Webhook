using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for authenticating users and managing account credentials.
/// </summary>
/// <remarks>
/// These endpoints allow clients to:
/// <list type="bullet">
///   <item><description>Authenticate with email or username and password to receive a JWT access token.</description></item>
///   <item><description>Change an existing account password after successful authentication.</description></item>
///   <item><description>Request a one-time password (OTP) for account operations such as password reset.</description></item>
///   <item><description>Request a refresh on a signed in user session..</description></item>
/// </list>
/// </remarks>
[Route("api/[controller]")]
[ApiController]
[AllowAnonymous]
public class AuthenticationController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuthenticationController"/> class.
    /// </summary>
    /// <param name="authenticationService">
    /// The service responsible for handling authentication operations including
    /// login, password management, and OTP generation.
    /// </param>
    public AuthenticationController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
        _logger = Log.ForContext(_className, nameof(AuthenticationController));
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger;

    /// <summary>
    /// Authenticates a user and returns a JWT access token and refresh token.
    /// </summary>
    /// <remarks>
    /// The <paramref name="loginUser"/> field <c>UserNameOrEmailAddress</c> accepts
    /// either a registered email address or a username. The service determines
    /// the lookup strategy based on whether the input contains an <c>@</c> character.
    ///
    /// On success, the response contains:
    /// <list type="bullet">
    ///   <item><description>An <c>AccessToken</c> — a signed JWT valid for the configured token lifetime.</description></item>
    ///   <item><description>A <c>RefreshToken</c> — a secure random token for renewing the access token.</description></item>
    /// </list>
    ///
    /// Failed login attempts are tracked. After the configured maximum number of
    /// consecutive failures the account is locked out for a fixed period.
    /// </remarks>
    /// <param name="loginUser">
    /// The login credentials containing the username or email address and the
    /// account password.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A <see cref="TokenDto"/> containing the access token and refresh token on success,
    /// or a descriptive error response on failure.
    /// </returns>
    /// <response code="200">Authentication succeeded. Access token and refresh token returned.</response>
    /// <response code="400">The account is locked out or sign-in is not permitted.</response>
    /// <response code="404">No account was found matching the provided credentials.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("login")]
    [ProducesResponseType(typeof(GenericResponse<TokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> LoginUser(
        [FromBody] LoginUserDto loginUser,
        CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(LoginUser));
        try
        {
            var result = await _authenticationService.LoginUserAsync(loginUser, ct);
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
    /// Changes the password for an authenticated user account.
    /// </summary>
    /// <remarks>
    /// The caller must supply the current password for verification before the
    /// new password is applied. This endpoint does not require a prior OTP — use
    /// <c>POST /api/Authentication/request-otp</c> followed by
    /// <c>POST /api/OtpOperation/validate-otp</c> for OTP-gated password resets.
    ///
    /// Password requirements are enforced by the Identity policy configured at
    /// application startup.
    /// </remarks>
    /// <param name="changePasswordRequest">
    /// Contains the user identifier, current password, and the desired new password.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response if the password was changed, or a descriptive error
    /// response if the current password is incorrect or the new password fails
    /// validation.
    /// </returns>
    /// <response code="200">Password changed successfully.</response>
    /// <response code="400">The current password is incorrect or the new password fails validation rules.</response>
    /// <response code="404">No account was found matching the provided identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto changePasswordRequest,
        CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ChangePassword));
        try
        {
            var result = await _authenticationService.ChangePasswordAsync(changePasswordRequest, ct);
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
    /// Generates and sends a one-time password (OTP) to the email address
    /// associated with the specified account.
    /// </summary>
    /// <remarks>
    /// This endpoint initiates an OTP flow for operations that require additional
    /// verification, such as a password reset. The generated OTP is sent via email
    /// using the configured SMTP service and is valid for 10 minutes.
    ///
    /// Once received, the OTP must be submitted to
    /// <c>POST /api/OtpOperation/validate-otp</c> to complete the operation.
    ///
    /// Requesting a new OTP while a valid one exists will invalidate the previous code.
    /// </remarks>
    /// <param name="requestOtpRequest">
    /// Contains the account identifier (email or username) and the purpose of the
    /// OTP request (e.g. password reset).
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the OTP was generated and dispatched, or
    /// a descriptive error response if the account could not be located.
    /// </returns>
    /// <response code="200">OTP generated and sent to the account email address.</response>
    /// <response code="404">No account was found matching the provided identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("request-otp")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RequestOTP(
        [FromBody] RequestOtpDto requestOtpRequest,
        CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RequestOTP));
        try
        {
            var result = await _authenticationService.RequestOtpAsync(requestOtpRequest, ct);
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
    /// Refreshes an authenticated user's session using a valid refresh token.
    /// </summary>
    /// <param name="tokenDetails">
    /// Contains the refresh token required to obtain a new authentication session.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A response containing the newly generated authentication tokens when the refresh
    /// operation is successful; otherwise, an appropriate error response.
    /// </returns>
    /// <response code="200">
    /// The refresh token is valid and a new authentication session has been generated.
    /// </response>
    /// <response code="400">
    /// The supplied refresh token could not be found or is no longer valid.
    /// </response>
    /// <response code="500">
    /// An unexpected error occurred while processing the refresh request.
    /// </response>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(GenericResponse<TokenDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<TokenDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<TokenDto>), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefreshSession([FromBody] TokenDto tokenDetails, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RefreshSession));
        try
        {
            var result = await _authenticationService.RefreshTokenAsync(tokenDetails, ct);
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