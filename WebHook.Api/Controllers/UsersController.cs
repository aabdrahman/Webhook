using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Api.Controllers;

/// <summary>
/// Provides endpoints for managing user accounts within the webhook service.
/// </summary>
/// <remarks>
/// These endpoints allow clients to:
/// <list type="bullet">
///   <item><description>Register a new user account.</description></item>
///   <item><description>Deactivate an existing user account to prevent login and access.</description></item>
///   <item><description>Reactivate a previously deactivated user account to restore access.</description></item>
/// </list>
///
/// All user management operations are subject to Identity validation rules
/// configured at application startup, including password complexity requirements
/// and username uniqueness constraints.
/// </remarks>
[Route("api/[controller]")]
[ApiController]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    /// <summary>
    /// Initializes a new instance of the <see cref="UsersController"/> class.
    /// </summary>
    /// <param name="userService">
    /// The service responsible for user account management operations including
    /// registration, deactivation, and reactivation.
    /// </param>
    public UsersController(IUserService userService)
    {
        _userService = userService;
        _logger = Log.ForContext(_className, nameof(UsersController));
    }

    private const string _className = "ClassName";
    private const string _methodName = "MethodName";
    private Serilog.ILogger _logger;

    /// <summary>
    /// Registers a new user account in the system.
    /// </summary>
    /// <remarks>
    /// Creates a new user account with the provided details and assigns the
    /// default <c>USER</c> role. Both the email address and username must be
    /// unique across all registered accounts.
    ///
    /// Password requirements are enforced by the Identity policy:
    /// <list type="bullet">
    ///   <item><description>Minimum length and complexity rules apply.</description></item>
    ///   <item><description>Common or easily guessable passwords may be rejected.</description></item>
    /// </list>
    ///
    /// On success the account is immediately active and the user may log in via
    /// <c>POST /api/Authentication/login</c>.
    /// </remarks>
    /// <param name="createUserRequest">
    /// The registration details including first name, last name, email address,
    /// username, and password.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A 201 Created response on success, or a descriptive error response if
    /// the email or username is already taken, or the password fails validation.
    /// </returns>
    /// <response code="201">User account created successfully and assigned to the USER role.</response>
    /// <response code="400">The password does not meet the required complexity rules.</response>
    /// <response code="409">An account with the provided email address or username already exists.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    [HttpPost("register")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] CreateUserDto createUserRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(Register));
        try
        {
            var result = await _userService.CreateUserAsync(createUserRequest, ct);
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
    /// Deactivates a user account to prevent the user from logging in or
    /// accessing protected resources.
    /// </summary>
    /// <remarks>
    /// Deactivation is a soft operation — the account record is retained in the
    /// system but the user is marked inactive. Any active sessions or tokens
    /// issued prior to deactivation should be treated as expired by the caller.
    ///
    /// A justification for the deactivation must be provided and is stored
    /// against the user record for audit purposes.
    ///
    /// A deactivated account can be restored at any time via
    /// <c>POST /api/Users/reactivate</c>.
    /// </remarks>
    /// <param name="userDeactivationRequest">
    /// Contains the user identifier (email or username) and the justification
    /// for deactivation.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the account was deactivated, or a descriptive
    /// error response if the account could not be found or is already inactive.
    /// </returns>
    /// <response code="200">User account deactivated successfully.</response>
    /// <response code="400">The account is already inactive or the request is invalid.</response>
    /// <response code="404">No account was found matching the provided identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpPost("deactivate")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> Deactivate([FromBody] UserDeactivationRequestDto userDeactivationRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(Deactivate));
        try
        {
            var result = await _userService.DeactivateUserProfileAsync(userDeactivationRequest, ct);
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
    /// Reactivates a previously deactivated user account to restore login access.
    /// </summary>
    /// <remarks>
    /// Reactivation restores the user's ability to authenticate and access
    /// protected resources. The deactivation justification stored against the
    /// account is cleared on successful reactivation.
    ///
    /// This operation is only valid for accounts that are currently in an
    /// inactive state. Attempting to reactivate an already active account
    /// returns a 400 response.
    ///
    /// Following reactivation the user must log in via
    /// <c>POST /api/Authentication/login</c> to obtain a new access token —
    /// no tokens are issued by this endpoint.
    /// </remarks>
    /// <param name="reactivateUserRequest">
    /// Contains the user identifier (email or username) to identify the account
    /// to be reactivated.
    /// </param>
    /// <param name="ct">
    /// A cancellation token that can be used to cancel the operation before it completes.
    /// </param>
    /// <returns>
    /// A success response confirming the account was reactivated, or a descriptive
    /// error response if the account could not be found or is already active.
    /// </returns>
    /// <response code="200">User account reactivated successfully. The user may now log in.</response>
    /// <response code="400">The account is already active or the request is invalid.</response>
    /// <response code="404">No account was found matching the provided identifier.</response>
    /// <response code="500">An unexpected server error occurred.</response>
    /// <response code="429">Too many reqeusts within the configured rate limit.</response>
    [HttpPost("reactivate")]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(GenericResponse<string>), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(typeof(GenericResponse<object>), StatusCodes.Status429TooManyRequests)]
    [Authorize(Roles = "Admin")]
    [EnableRateLimiting("per-user-rating")]
    public async Task<IActionResult> Reactivate([FromBody] ReactivateUserRequestDto reactivateUserRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(Reactivate));
        try
        {
            var result = await _userService.ReactivateUserProfileAsync(reactivateUserRequest, ct);
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