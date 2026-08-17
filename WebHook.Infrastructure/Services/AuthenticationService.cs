using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.Services;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly UserManager<User> _userManager;
    private readonly RepositoryContext _repositoryContext;
    private readonly JwtSettingsConfiguration _jwtSettingsConfiguration;
    private readonly SignInManager<User> _signInManager;
    private readonly IOtpGenerator _otpGenerator;
    private readonly IOptionsMonitor<OtpSettingsConfiguration> _otpettingsOptionsMonitor;
    private readonly IOptionsMonitor<TokenValidationConfiguration> _tokenValidationOptionsMonitor;
    private readonly IApplicationHasher _applicationHasher;
    private readonly IEmailService _emailService;
    private readonly EmailContentFormatterHelper _emailContentFormatterHelper;

    public AuthenticationService(UserManager<User> userManager, RepositoryContext repositoryContext,
                                IOptionsMonitor<JwtSettingsConfiguration> jwtSettingsOptionsMonitor, SignInManager<User> signInManager,
                                IOtpGenerator otpGenerator, IOptionsMonitor<OtpSettingsConfiguration> otpettingsOptionsMonitor,
                                IOptionsMonitor<TokenValidationConfiguration> tokenValidationOptionsMonitor, IApplicationHasher applicationHasher,
                                IEmailService emailService, EmailContentFormatterHelper emailContentFormatterHelper)
    {
        _userManager = userManager;
        _repositoryContext = repositoryContext;
        _jwtSettingsConfiguration = jwtSettingsOptionsMonitor.CurrentValue;
        _signInManager = signInManager;
        _otpGenerator = otpGenerator;
        _otpettingsOptionsMonitor = otpettingsOptionsMonitor;
        _tokenValidationOptionsMonitor = tokenValidationOptionsMonitor;
        _applicationHasher = applicationHasher;
        _emailService = emailService;

        _logger = Log.ForContext(_className, nameof(AuthenticationService));
        _emailContentFormatterHelper = emailContentFormatterHelper;
    }

    private Serilog.ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    private User? _loggedInUser;

    public async Task<GenericResponse<TokenDto>> LoginUserAsync(LoginUserDto loginUserDetails, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(LoginUserAsync));

        try
        {
            _logger.Information("Login user request - {0}", loginUserDetails);

            User? userToAuthenticate = loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                await _userManager.FindByEmailAsync(loginUserDetails.UserNameOrEmailAddress) :
                await _userManager.FindByNameAsync(loginUserDetails.UserNameOrEmailAddress);

            if (userToAuthenticate is null)
            {
                _logger.Warning(loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "User with email does not exists - {0}" :
                                "User with username does not exists - {0}", loginUserDetails.UserNameOrEmailAddress
                    );

                return GenericResponse<TokenDto>.Failure(null, loginUserDetails.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "Invalid Credentials." : "Invalid Credentials.",
                    HttpStatusCode.NotFound);
            }

            //This is disabled because the identity provider provides default failed lockout increment in signin manager which is not available when this is enabled.
            //bool isPasswordValid = await _userManager.CheckPasswordAsync(userToAuthenticate, loginUserDetails.Password);

            //if (!isPasswordValid)
            //{
            //    _logger.Warning("User details does not match the provided password - {0}", loginUserDetails.UserNameOrEmailAddress);
            //    return GenericResponse<TokenDto>.Failure(null, "Invalid Credentials.", HttpStatusCode.NotFound);
            //}

            _logger.Information("User details successfully validated. Begin user system signin operation");
            SignInResult signinResult = await _signInManager.CheckPasswordSignInAsync(userToAuthenticate, loginUserDetails.Password, true);

            if (signinResult.RequiresTwoFactor)
            {
                _logger.Warning("Invalid User created - {0}, {1}. User created with 2FA enabled.", userToAuthenticate.NormalizedEmail, userToAuthenticate.NormalizedUserName);
                return GenericResponse<TokenDto>.Failure(null, "Invalid Profile. Contact Admin.", HttpStatusCode.Conflict);
            }

            if (signinResult.IsLockedOut)
            {
                _logger.Warning("User could not be signed in successfully. Is locked out - {0}, Is Not Allowed - {1}", signinResult.IsLockedOut, signinResult.IsNotAllowed);
                return GenericResponse<TokenDto>.Failure(null, "User profiled locked out. Kindly contact admin or reset your password.", HttpStatusCode.BadRequest);
            }


            if (signinResult.IsNotAllowed)
            {
                _logger.Warning("User could not be signed in successfully. Is locked out - {0}, Is Not Allowed - {1}", signinResult.IsLockedOut, signinResult.IsNotAllowed);
                return GenericResponse<TokenDto>.Failure(null, "User could not be logged in. Kindly contact admin.", HttpStatusCode.BadRequest);
            }

            if (!signinResult.Succeeded)
            {
                _logger.Warning("User signin failed - {0}", loginUserDetails.UserNameOrEmailAddress);
                return GenericResponse<TokenDto>.Failure(null, "Invalid Credentials.", HttpStatusCode.BadRequest);
            }


            _logger.Information("User signed in successfully. Begin token generation for user.");
            _loggedInUser = userToAuthenticate;
            DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;

            _loggedInUser.RefreshToken = GenerateRefreshToken();
            _loggedInUser.TokenExpirationTime = operationTimestamp.AddSeconds(_jwtSettingsConfiguration.RefreshTokenExpirationAfterInSeconds);
            _loggedInUser.LastLoginDate = operationTimestamp;
            _loggedInUser.LastAuthenticatedAt = operationTimestamp;

            string token = await GenerateToken();

            var updateUserResult = await _userManager.UpdateAsync(_loggedInUser);

            if (!updateUserResult.Succeeded)
            {
                _logger.Warning("User details could not be saved successfully after token geenration. rrors - {0}", updateUserResult.Errors);
                return GenericResponse<TokenDto>.Failure(null, "An error occurred eprforming operation.", HttpStatusCode.InternalServerError);
            }

            var tokenDetails = new TokenDto(accessToken: token, refreshToken: _loggedInUser.RefreshToken);

            _logger.Information("User with details - {0} aigned in successfully and token generated.", loginUserDetails.UserNameOrEmailAddress);

            return GenericResponse<TokenDto>.Success(tokenDetails, "User signed in successfully.", HttpStatusCode.OK);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred performing user login request.");
            return GenericResponse<TokenDto>.Failure(null, "An error occurred.", HttpStatusCode.InternalServerError);
        }
    }


    public async Task<GenericResponse<string>> ChangePasswordAsync(ChangePasswordDto changePasswordRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ChangePasswordAsync));

        try
        {
            _logger.Information("Change password request for - {0}", changePasswordRequest);

            User? userToModifyPassword = await _userManager.FindByEmailAsync(changePasswordRequest.UserNameOrEmailAddress);

            if (userToModifyPassword is null)
            {
                _logger.Warning("User with email does not exist - {0}", changePasswordRequest.UserNameOrEmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            //bool isOldPasswordCorrect = await _userManager.CheckPasswordAsync(userToModifyPassword, changePasswordRequest.OldPassword);

            //if (!isOldPasswordCorrect)
            //{
            //    _logger.Warning("Change password request could not be completed for user: {0}, {1}. Invalid old password provided.", userToModifyPassword.Id, userToModifyPassword.NormalizedEmail);
            //    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Password provided.", HttpStatusCode.BadRequest);
            //}

            IdentityResult setPasswordResult = await _userManager.ChangePasswordAsync(userToModifyPassword, changePasswordRequest.OldPassword, changePasswordRequest.NewPassword);
            if (!setPasswordResult.Succeeded)
            {
                _logger.Warning("Identity Server could not process change password request. Error - {0}", setPasswordResult.Errors.ToList());
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials.", HttpStatusCode.BadRequest);
            }

            userToModifyPassword.RefreshToken = "";
            IdentityResult updateRecordResult = await _userManager.UpdateAsync(userToModifyPassword);

            if (!updateRecordResult.Succeeded)
            {
                _logger.Warning("Refresh token could not be revoked for the user: {0}. Error - {1}", userToModifyPassword.NormalizedEmail, updateRecordResult.Errors.ToList());
            }

            _logger.Information("Password Change processed successfullyfor user - {0}", userToModifyPassword.NormalizedEmail);
            return GenericResponse<string>.Success("Operation Successful.", "Password updated successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred performing user password change.");
            return GenericResponse<string>.Failure("Operation Failed.", "Operation could not be completed.", HttpStatusCode.InternalServerError);
        }

    }

    public async Task<GenericResponse<string>> RequestOtpAsync(RequestOtpDto requestOtp, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(RequestOtpAsync));

        try
        {
            _logger.Information("Request OTP - {0}", requestOtp);

            User? requestingUser = requestOtp.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                                   await _userManager.FindByEmailAsync(requestOtp.UserNameOrEmailAddress) :
                                    await _userManager.FindByNameAsync(requestOtp.UserNameOrEmailAddress);

            if (requestingUser is null)
            {
                _logger.Warning("User with details does not exist - {0}", requestOtp.UserNameOrEmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", "User with details does not exist.", HttpStatusCode.NotFound);
            }

            string generatedOTP = _otpGenerator.GenerateOtp(_otpettingsOptionsMonitor.CurrentValue.OtpToGenerateLength, _otpettingsOptionsMonitor.CurrentValue.MaximumOtpLength);
            if (string.IsNullOrEmpty(generatedOTP))
            {
                _logger.Warning("OTP could not be generated.");
                return GenericResponse<string>.Failure("Operation Failed.", "Operation could not be completed. Kindly retry.", HttpStatusCode.FailedDependency);
            }

            string hashedOTP = await _applicationHasher.HashSecret(generatedOTP);
            if (string.IsNullOrEmpty(hashedOTP))
            {
                _logger.Warning("An error occurred while hashing the OTP. Hash returns - {0}", hashedOTP);
                return GenericResponse<string>.Failure("Operation Failed.", "Operation could not be completed. Kindly retry.", HttpStatusCode.FailedDependency);
            }

            OtpVerification otpVerification = new OtpVerification()
            {
                UserId = requestingUser.Id,
                Purpose = requestOtp.Purpose,
                OtpHash = hashedOTP,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_tokenValidationOptionsMonitor.CurrentValue.OtpExpirationAfterInSeconds)
            };

            await _repositoryContext.OtpVerifications.AddAsync(otpVerification, ct);
            await _repositoryContext.SaveChangesAsync(ct);

            //Begin sending email operation
            string subject = requestOtp.Purpose switch
            {
                OtpPurpose.PasswordReset => "Password Reset Request",
                _ => ""
            };

            string label = requestOtp.Purpose switch
            {
                OtpPurpose.PasswordReset => "Password Reset",
                _ => ""
            };

            string title = requestOtp.Purpose switch
            {
                OtpPurpose.PasswordReset => "You requested a password reset",
                _ => ""
            };

            string description = requestOtp.Purpose switch
            {
                OtpPurpose.PasswordReset => "We received a request to reset the password for your account...",
                _ => ""
            };

            Dictionary<string, string> emailTemplateParameter = new Dictionary<string, string>()
            {
                { "NotificationTimestamp", DateTimeOffset.UtcNow.ToLocalTime().ToString("F") },
                { "FirstName", requestingUser.FirstName  },
                { "EmailAddress", requestingUser.NormalizedEmail!.ToLower() },
                { "OtpCode", generatedOTP },
                { "RequestedAt", otpVerification.CreatedAt.ToLocalTime().ToString("F") },
                { "ExpiresAt", otpVerification.ExpiresAt.ToLocalTime().ToString("F")  },
                { "SupportEmail", "support@webhook.com" },
                { "OtpExpiryMinutes", TimeSpan.FromSeconds(_tokenValidationOptionsMonitor.CurrentValue.OtpExpirationAfterInSeconds).Minutes.ToString("G") },
                { "OtpPurposeLabel", label },
                { "OtpPurposeTitle", title },
                { "OtpPurposeDescription", description }
            };

            string? emailContent = await _emailContentFormatterHelper.GetEmailContentAsync(NotificationType.SendOtpNotification, emailTemplateParameter);
            bool queueEmailResult = false;

            if (!string.IsNullOrEmpty(emailContent))
            {
                EmailSenderDto emailSenderItem = new EmailSenderDto(MailContent: emailContent, Subject: subject, MailRecipients: [requestingUser.NormalizedEmail!], IsHtml: true);
                queueEmailResult = await _emailService.SendMailAsync(emailSenderItem, ct);
            }


            _logger.Information("User OTP requested successfully. Requested OTP - {0}. Queue Email response: {1}. Email COntent Generated - {2}", generatedOTP, queueEmailResult, !string.IsNullOrEmpty(emailContent));
            return GenericResponse<string>.Success("Operation Successful.", "OTP sent successfully.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while requesting for OTP.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred requesting for OTP. Kindly retry.", HttpStatusCode.InternalServerError);
        }

    }

    //-------------------------------------------------
    // Utility operation class sccoped methods.
    //-------------------------------------------------

    private async Task<List<Claim>> GetUserClaims()
    {
        var claims = new List<Claim>();
        var roles = await _userManager.GetRolesAsync(_loggedInUser!);

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        claims.Add(new Claim(ClaimTypes.Email, _loggedInUser?.NormalizedEmail!));
        claims.Add(new Claim(ClaimTypes.Name, _loggedInUser?.NormalizedUserName!));
        claims.Add(new Claim(ClaimTypes.NameIdentifier, _loggedInUser?.Id.ToString()!));
        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        claims.Add(new Claim(JwtRegisteredClaimNames.Sub, _loggedInUser?.Id.ToString()!));

        return claims;
    }

    private SigningCredentials GetSigninCredentials()
    {
        string secretKey = Environment.GetEnvironmentVariable("webhook_secret_key") ?? throw new ArgumentNullException("Application secret key is not yet defined.");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

        return new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

    }

    private async Task<string> GenerateToken()
    {
        var userClaims = await GetUserClaims();
        var tokenCredentials = GetSigninCredentials();
        var tokenOptions = GetTokenOptions(tokenCredentials, userClaims);

        var tokenHandler = new JwtSecurityTokenHandler();

        return tokenHandler.WriteToken(tokenOptions);
    }

    private JwtSecurityToken GetTokenOptions(SigningCredentials tokenCredentials, List<Claim> userClaims)
    {
        var tokenOptions = new JwtSecurityToken
        (
            issuer: _jwtSettingsConfiguration.ValidIssuer,
            audience: "",
            claims: userClaims,
            expires: DateTime.UtcNow.AddSeconds(_jwtSettingsConfiguration.TokenExpirationAfterInSeconds),
            signingCredentials: tokenCredentials
        );

        return tokenOptions;
    }

    private string GenerateRefreshToken()
    {
        var rndNumBytes = new byte[32];

        using (var rndNumGen = RandomNumberGenerator.Create())
        {
            rndNumGen.GetBytes(rndNumBytes);
        }

        return Convert.ToBase64String(rndNumBytes);
    }
}
