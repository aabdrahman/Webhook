using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.DataTransferObjects.OtpOperation;
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
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;
    private readonly IDataProtector _dataProtector;

    public AuthenticationService(UserManager<User> userManager, RepositoryContext repositoryContext,
                                IOptionsMonitor<JwtSettingsConfiguration> jwtSettingsOptionsMonitor, SignInManager<User> signInManager,
                                IOtpGenerator otpGenerator, IOptionsMonitor<OtpSettingsConfiguration> otpettingsOptionsMonitor,
                                IOptionsMonitor<TokenValidationConfiguration> tokenValidationOptionsMonitor, IApplicationHasher applicationHasher,
                                IEmailService emailService, EmailContentFormatterHelper emailContentFormatterHelper, IAuthenticatedUserDetails authenticatedUserDetails, IDataProtectionProvider dataProtectionProvider)
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
        _emailContentFormatterHelper = emailContentFormatterHelper;
        _authenticatedUserDetails = authenticatedUserDetails;
        _dataProtector = dataProtectionProvider.CreateProtector("Webhook.Otp.OtpVerificationSigning");

        _logger = Log.ForContext(_className, nameof(AuthenticationService));
        
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


    public async Task<GenericResponse<string>> ResetUserPasswordAsync(ResetUserPasswordequestDto resetUserPasswordequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ResetUserPasswordAsync));

        try
        {
            //Begin the operation to reset user password.
            _logger.Information("Reset User password request - {0}", resetUserPasswordequest);

            //Fetch the token from the http context via the abstracted interface for authenticated user.
            string passwordResetToken = _authenticatedUserDetails.operationToken;

            //Check if the operation token is null or empty.
            if (string.IsNullOrEmpty(passwordResetToken))
            {
                //Operation token is empty or null, hence the system returns failure
                _logger.Warning("User operation token could not be obtained from the request header.");
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Token exist for the request, hence, use the dataprotector to unprotect it.
            string resetTokenSerializedDetails = _dataProtector.Unprotect(passwordResetToken);
            OtpVerificationSigning? resetTokenDetails = JsonSerializer.Deserialize<OtpVerificationSigning>(resetTokenSerializedDetails, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

            //The deserialized reset token can then be used. Validate that the deserilized item is not null.
            if(resetTokenDetails is null)
            {
                _logger.Warning("Serailized Operation Reset Token could not be deserialized accordingly. Unprotected details - {0}", resetTokenSerializedDetails);
                return GenericResponse<string>.Failure("Operation Failed", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Check the Jti guid and parse to guid successfully.
            if(!Guid.TryParse(resetTokenDetails.Jti, out Guid tokenJti))
            {
                //The jti from unprotected token could not be parsed as guid, hence, its probably been tampered with.
                _logger.Warning("The deserialized operation reset token jti could not be parsed as guid appropriately. Token details - {0}", resetTokenDetails);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Use the jti to query the OTPOperationTokens table and only get the tokens that are yet to expire, not consumed yet alongside the linked user and the OTP that is validated for it.
            OtpOperationToken? signedTokenFromDb = await _repositoryContext.OtpOperationTokens
                .Include(x => x.OtpVerification).Include(x => x.UserToPerformOperation)
                .FirstOrDefaultAsync(x => x.Jti == tokenJti && !x.RevokedAt.HasValue && !x.ConsumedAt.HasValue && x.ExpiresAt > DateTimeOffset.UtcNow, ct);

            //Check if the fetched token query returns null, this means that either the jti does not exist, its expired or its already consumed.
            if(signedTokenFromDb is null)
            {
                _logger.Error("Linked signed token in databased could not befetched for the parsed jti - {0}", resetTokenDetails.Jti);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Check that the purpose for creating the otp operation token tallies with this operation: PasswordReset
            if(signedTokenFromDb.Purpose != OtpPurpose.PasswordReset)
            {
                _logger.Warning("The provided operation token was created for another purpose. Token Purpose: {0}", signedTokenFromDb.Purpose.ToString());
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Validates that the token is yet to expire, this is a second guard though as the database query is more of the source of truth after the unprotect from the dataprotector.
            if(signedTokenFromDb.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _logger.Warning("Signed token is valid but has expired at: {0}", signedTokenFromDb.ExpiresAt);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //User? userToUpdate = await _userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == signedTokenFromDb.UserId, ct);

            //Check if the linked user is not empty, this ensures there is an item to reset password for.
            if (signedTokenFromDb.UserToPerformOperation is null)
            {
                _logger.Warning("The provided signed token was geenrated for an invalid user. Token probably tampered with as user could not be fetched. User ID - {0}", signedTokenFromDb.UserId);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Validate that the hashed token from the db corresponds with what is passed b the user, using the same hashing algorithm that is used to hash it in the first place.
            bool operationTokenFromDbValid = await _applicationHasher.ValidateHashedSecret(signedTokenFromDb.TokenHash, passwordResetToken);

            //If it returns false, then, the token is invalid and operation cannot proceed.
            if (!operationTokenFromDbValid)
            {
                _logger.Warning("The hashed token in the database does not correspond with the token from the header. Hashing Vlaidator returns - {0}", operationTokenFromDbValid);
                return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //IdentityResult removePasswordIdentity = await _userManager.RemovePasswordAsync(userToUpdate);

            //if (!removePasswordIdentity.Succeeded)
            //{
            //    _logger.Warning("Password could not be removed from user profile. Errors - {0}", removePasswordIdentity.Errors.ToList());
            //    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            //}

            //IdentityResult setPasswordIdentityResult = await _userManager.AddPasswordAsync(userToUpdate, resetUserPasswordequest.NewPassword);
            //if (!setPasswordIdentityResult.Succeeded)
            //{
            //    _logger.Warning("New password could not be set for user. Errors - {0}", setPasswordIdentityResult.Errors.ToList());
            //    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
            //}

            //Using the custom password validator to ensure that the new password corrsponds with what the identity is configured for.
            //This is a guard even though our reex shoudl work.
            IdentityResult passwordValidationResult = await ValidateUserPassword(signedTokenFromDb.UserToPerformOperation, resetUserPasswordequest.NewPassword);

            //if it retuns false, then, we can return invalid password for the user.
            if (!passwordValidationResult.Succeeded)
            {
                _logger.Warning("Password reset failed password policy validation. Errors: {0}", passwordValidationResult.Errors.ToList());
                return GenericResponse<string>.Failure("Operation Failed.", "The provided password does not meet the password requirements.", HttpStatusCode.BadRequest);
            }

            if(!string.Equals(resetTokenDetails.IssuedFor, signedTokenFromDb.UserToPerformOperation.NormalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("The signed operation token was issued to: {0} but the token from db ws issued for: {0}", resetTokenDetails.IssuedFor, signedTokenFromDb.UserToPerformOperation.NormalizedEmail);
                return GenericResponse<string>.Failure("Operation Failed.", "The provided password does not meet the password requirements.", HttpStatusCode.BadRequest);
            }

            //Begin the operation to update our token details, set to consumed and the consummation timestamp on both the otp and otp operation token table.
            DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;

            signedTokenFromDb.UserToPerformOperation.PasswordHash = _userManager.PasswordHasher.HashPassword(signedTokenFromDb.UserToPerformOperation, resetUserPasswordequest.NewPassword);
            signedTokenFromDb.UserToPerformOperation.IsActive = true;
            signedTokenFromDb.UserToPerformOperation.LockoutEnd = null;
            signedTokenFromDb.UserToPerformOperation.AccessFailedCount = 0;
            signedTokenFromDb.UserToPerformOperation.RefreshToken = string.Empty;
            signedTokenFromDb.UserToPerformOperation.TokenExpirationTime = null;
            signedTokenFromDb.OtpVerification.IsConsumed = true;
            signedTokenFromDb.OtpVerification.ConsumedAt = operationTimestamp;
            signedTokenFromDb.ConsumedAt = operationTimestamp;

            await _repositoryContext.SaveChangesAsync(ct);

            //All operation successful, hence we log success and return to client.
            _logger.Information("User password reset successfully. Token successfully consumed. User updated - {0}", signedTokenFromDb.UserToPerformOperation.Id);

            return GenericResponse<string>.Success("Operation Successful.", "Password reset successfully. Kindly proceed to login.", HttpStatusCode.OK);
            
        }
        catch(CryptographicException ex)
        {
            _logger.Error(ex, "An error occurred while decrypting the operation token.");
            return GenericResponse<string>.Failure("Operation Failed.", "Invalid Credentials. Kindly retry.", HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while resetting user password.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while resetting user password, kindly retry.", HttpStatusCode.InternalServerError);
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

    private async Task<IdentityResult> ValidateUserPassword(User userToUpdatePassword, string password)
    {
        foreach (var passwordValidator in _userManager.PasswordValidators)
        {
            var result = await passwordValidator.ValidateAsync(_userManager, userToUpdatePassword, password);

            if (!result.Succeeded)
            {
                return result;
            }
        }

        return IdentityResult.Success;
    }
}
