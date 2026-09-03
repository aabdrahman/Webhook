using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.DataTransferObjects.OtpOperation;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly RepositoryContext _repositoryContext;
    private readonly IAuthenticatedUserDetails _authenticatedUserDetails;
    private readonly IDataProtector _dataProtector;
    private readonly IApplicationHasher _applicationHasher;

    public UserService(RepositoryContext repositoryContext, UserManager<User> userManager, IAuthenticatedUserDetails authenticatedUserDetails, IDataProtectionProvider dataProtectionProvider, IApplicationHasher applicationHasher)
    {
        _repositoryContext = repositoryContext;
        _userManager = userManager;
        _dataProtector = dataProtectionProvider.CreateProtector("Webhook.Otp.OtpVerificationSigning");
        _authenticatedUserDetails = authenticatedUserDetails;
        _applicationHasher = applicationHasher;

        _logger = Log.ForContext(_className, nameof(UserService));

    }

    private Serilog.ILogger _logger;
    private const string _methodName = "MethodName";
    private const string _className = "ClassName";

    public async Task<GenericResponse<string>> CreateUserAsync(CreateUserDto createUser, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(CreateUserAsync));

        try
        {
            _logger.Information("Creating User with details - {0}", createUser);

            //Validate that email is unique.
            //var isUserExists = await _repositoryContext.Users.AnyAsync(x => x.NormalizedEmail == createUser.EmailAddress.ToUpper() || x.NormalizedUserName == createUser.UserName.ToUpper(), ct);

            //var validationResult = await _repositoryContext.Users.Select(x => new
            //{
            //    UserNameExists =  _repositoryContext.Users.Any(x => x.NormalizedUserName == createUser.UserName.ToUpper()),
            //    EmailExists = _repositoryContext.Users.Any(x => x.NormalizedEmail == createUser.EmailAddress.ToUpper())
            //}).FirstOrDefaultAsync(ct);

            if(createUser.UserName.Contains("@", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Warning("Cannot create user as the username contains the character: @ - {0}", createUser.UserName);
                return GenericResponse<string>.Failure("Operation Failed.", "Username cannot contan the special character: @", HttpStatusCode.BadRequest);
            }

            var existingUserWithEmail = await _userManager.FindByEmailAsync(createUser.EmailAddress);

            if (existingUserWithEmail is not null)
            {
                _logger.Warning("User with email already exists - {0}", createUser.EmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", "User with email alraady exists.", HttpStatusCode.Conflict);
            }

            var existingUserWithUserName = await _userManager.FindByNameAsync(createUser.UserName);

            if (existingUserWithUserName is not null)
            {
                _logger.Warning("User with username already exists.");
                return GenericResponse<string>.Failure("Operation Failed.", "Username already taken.", HttpStatusCode.Conflict);
            }

            User userToCreate = createUser.ToEntity();

            IdentityResult userCreationResult = await _userManager.CreateAsync(userToCreate, password: createUser.Password);

            //Check if user is created successfully with provided details.
            if (!userCreationResult.Succeeded)
            {
                var errors = userCreationResult.Errors.ToList();
                _logger.Warning("System could not create user successfully. Errors - {0}", errors);
                return GenericResponse<string>.Failure("Operation Failed.", "Your profile could not be created. Kindly retry.", HttpStatusCode.BadRequest);
            }

            //Begin setting the roles for user.
            try
            {
                IdentityResult setRolesResult = await _userManager.AddToRoleAsync(userToCreate, "USER");

                if (!setRolesResult.Succeeded)
                {
                    _logger.Warning("Role could not be added to the created user.", setRolesResult.Errors.ToList());
                    return GenericResponse<string>.Success("Operation Successful.", "User created.", HttpStatusCode.Created);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred adding role to user.");
                return GenericResponse<string>.Success("Operation Successful.", "User created.", HttpStatusCode.Created);
            }


            //Roles successfully created for the user. returning success response.
            _logger.Information("User created successfully an droles successfully maintained for user");
            return GenericResponse<string>.Success("Operation Successful.", "User created successfully.", HttpStatusCode.Created);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while creating user.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while creating user.", HttpStatusCode.InternalServerError,
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }

    public async Task<GenericResponse<string>> DeactivateUserProfileAsync(UserDeactivationRequestDto userDeactivationRequest, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(DeactivateUserProfileAsync));

        try
        {
            _logger.Information("Deactivate User profile request - {0}", userDeactivationRequest);

            string operationToken = _authenticatedUserDetails.operationToken;
            string userRole = _authenticatedUserDetails.assignedRole;
            bool isAdmin = userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);

            if (!isAdmin)
            {
                if (string.IsNullOrWhiteSpace(operationToken))
                {
                    _logger.Warning("User deactivation could not be processed as the operation token is not passed.");
                    return GenericResponse<string>.Failure("Operation Failed.", "Credentials not provided.", HttpStatusCode.Forbidden);
                }

                string unprotectedToken = _dataProtector.Unprotect(operationToken);
                OtpVerificationSigning? tokenDetails = JsonSerializer.Deserialize<OtpVerificationSigning>(unprotectedToken, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                if (tokenDetails is null)
                {
                    _logger.Warning("The unprotected token could not be parsed successfully.");
                    return GenericResponse<string>.Failure("Operation Failed.", "Token could not be parsed successfully. Kindly re-authenticate.", HttpStatusCode.BadRequest);
                }

                if(!string.Equals(_authenticatedUserDetails.emailAddress, tokenDetails.IssuedFor, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning("Authenticated user: {0} is using another user operation token: {1}", _authenticatedUserDetails.emailAddress, tokenDetails.IssuedFor);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }

                if (!Guid.TryParse(tokenDetails.Jti, out var tokenJti))
                {
                    _logger.Warning("The provided jti from the operation token could not be parsed as a valid guid - {0}", tokenDetails.Jti);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }

                OtpOperationToken? operationTokenDetailsFromDb = await _repositoryContext.OtpOperationTokens
                                                            .OrderByDescending(x => x.CreatedAt)
                                                            .Include(x => x.OtpVerification)
                                                            .Where(x => !x.ConsumedAt.HasValue && x.ExpiresAt >= DateTimeOffset.UtcNow && !x.RevokedAt.HasValue && x.OtpVerification.ValidatedAt.HasValue)
                                                            .FirstOrDefaultAsync(x => x.Jti == tokenJti, ct);

                if (operationTokenDetailsFromDb is null)
                {
                    _logger.Warning("The parsed jti from operation token coudl not be verified from database - {0}", tokenDetails.Jti);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }

                if(operationTokenDetailsFromDb.Purpose != Core.Constants.OtpPurpose.DeactivateProfile)
                {
                    _logger.Warning("Provided operation token was issued for another purpose: {0}, Expected purpose: {1}", operationTokenDetailsFromDb.Purpose.ToString(), Core.Constants.OtpPurpose.DeactivateProfile);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }

                bool isValidToken = await _applicationHasher.ValidateHashedSecret(operationTokenDetailsFromDb.TokenHash, operationToken);

                if (!isValidToken)
                {
                    _logger.Warning("The hashed token for the provided token jti could not be validated with the application hasher.");
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }



                User? userToDeactivate = userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                                        await _userManager.FindByEmailAsync(userDeactivationRequest.UserNameOrEmailAddress) :
                                        await _userManager.FindByNameAsync(userDeactivationRequest.UserNameOrEmailAddress);

                if (userToDeactivate is null)
                {
                    _logger.Warning(userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "User with email does not exist - {0}" : "User with provided user name does not exist - {0}", userDeactivationRequest.UserNameOrEmailAddress);
                    return GenericResponse<string>.Failure("Operation Failed.", $"User with details does not exist - {userDeactivationRequest.UserNameOrEmailAddress}", HttpStatusCode.NotFound);
                }

                if(!userToDeactivate.NormalizedEmail!.Equals(tokenDetails.IssuedFor, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Warning("Could not proceed as the token is issued for another user. Issued For: {0}, User Email: {1}", tokenDetails.IssuedFor, userToDeactivate.NormalizedEmail);
                    return GenericResponse<string>.Failure("Operation Failed.", "Invalid Token provided.", HttpStatusCode.BadRequest);
                }

                DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;
                //Update the user details
                userToDeactivate.IsActive = false;
                userToDeactivate.DeactivationJustification = userDeactivationRequest.DeactivationJustification;
                userToDeactivate.DeletedAt = operationTimestamp;
                userToDeactivate.DeletedByUserId = _authenticatedUserDetails.userId;
                userToDeactivate.LockoutEnd = DateTimeOffset.UtcNow.AddDays(3525);

                //Update the extracted token details
                operationTokenDetailsFromDb.OtpVerification.ConsumedAt = operationTimestamp;
                operationTokenDetailsFromDb.OtpVerification.IsConsumed = true;
                operationTokenDetailsFromDb.ConsumedAt = operationTimestamp;

                //IdentityResult deactivateResult = await _userManager.UpdateAsync(userToDeactivate);

                //if (!deactivateResult.Succeeded)
                //{
                //    _logger.Warning("User profile could not be deactivated. Errors - {0}", deactivateResult.Errors.ToList());
                //    return GenericResponse<string>.Failure("Operation Failed.", "User profile could not be deactivated. Kindly retry.", HttpStatusCode.BadRequest);
                //}

                await _repositoryContext.SaveChangesAsync(ct);

                _logger.Information("User profile successfully deactivated - {0}", userDeactivationRequest.UserNameOrEmailAddress);
                return GenericResponse<string>.Success("Operation Successful.", "User profile successfully deactivated.", HttpStatusCode.OK);
            }
            else
            {
                _logger.Information("Begin deactivation operation for admin user... {0}", _authenticatedUserDetails.userId);
                User? userToDeactivate = userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                                        await _userManager.FindByEmailAsync(userDeactivationRequest.UserNameOrEmailAddress) :
                                        await _userManager.FindByNameAsync(userDeactivationRequest.UserNameOrEmailAddress);

                if (userToDeactivate is null)
                {
                    _logger.Warning(userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "User with email does not exist - {0}" : "User with provided user name does not exist - {0}", userDeactivationRequest.UserNameOrEmailAddress);
                    return GenericResponse<string>.Failure("Operation Failed.", $"User with details does not exist - {userDeactivationRequest.UserNameOrEmailAddress}", HttpStatusCode.NotFound);
                }

                DateTimeOffset operationTimestamp = DateTimeOffset.UtcNow;
                //Update the user details
                userToDeactivate.IsActive = false;
                userToDeactivate.DeactivationJustification = userDeactivationRequest.DeactivationJustification;
                userToDeactivate.DeletedAt = operationTimestamp;
                userToDeactivate.DeletedByUserId = _authenticatedUserDetails.userId;
                userToDeactivate.LockoutEnd = DateTimeOffset.UtcNow.AddDays(3525);

                await _repositoryContext.SaveChangesAsync(ct);

                _logger.Information("User profile successfully deactivated - {0}", userDeactivationRequest.UserNameOrEmailAddress);
                return GenericResponse<string>.Success("Operation Successful.", "User profile successfully deactivated.", HttpStatusCode.OK);

            }
            

        }
        catch(CryptographicException ex)
        {
            _logger.Error(ex, "An error occurred while deactivating user. Operation token could not be validated.");
            return GenericResponse<string>.Failure("Operation Failed.", "Token has expired. Kindly re-authenticate.", HttpStatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while deactivating user profile.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while deactivating user profile. Kindly retry.", HttpStatusCode.InternalServerError);
        }
    }

    public async Task<GenericResponse<string>> ReactivateUserProfileAsync(ReactivateUserRequestDto reactivateUser, CancellationToken ct = default)
    {
        _logger = _logger.ForContext(_methodName, nameof(ReactivateUserProfileAsync));
        try
        {
            _logger.Information("Reactivate User Profile request - {0}", reactivateUser);

            var userToReactivateQuery = _userManager.Users.IgnoreQueryFilters();

            if (reactivateUser.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase))
            {
                userToReactivateQuery = userToReactivateQuery.Where(x => x.NormalizedEmail.Contains(reactivateUser.UserNameOrEmailAddress.ToUpper()));
            }
            else
            {
                userToReactivateQuery = userToReactivateQuery.Where(x => x.NormalizedUserName.Contains(reactivateUser.UserNameOrEmailAddress.ToUpper()));
            }

            User? userToReactivate = await userToReactivateQuery.FirstOrDefaultAsync(ct);

            if (userToReactivate is null)
            {
                _logger.Warning("User profile with details - {0} does not exist.", reactivateUser.UserNameOrEmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", "User with provided details does not exist.", HttpStatusCode.NotFound);
            }

            if (userToReactivate.IsActive)
            {
                _logger.Warning("User profile with identifier - {0} is already active. Current Status - {0}", reactivateUser.UserNameOrEmailAddress, userToReactivate.IsActive);
                return GenericResponse<string>.Failure("Operation Failed.", "User profile is currently active.", HttpStatusCode.Conflict);
            }

            userToReactivate.IsActive = true;
            IdentityResult reactivateResult = await _userManager.UpdateAsync(userToReactivate);

            if (!reactivateResult.Succeeded)
            {
                _logger.Warning("User profile could not be reactivated by the system identity provider. Errors - {0}", reactivateResult.Errors.ToList());
                return GenericResponse<string>.Failure("Operation Failed.", "User profile deactivation failed. Kindly retry.", HttpStatusCode.BadRequest);
            }

            _logger.Information("User with profile identifier - {0} has been successfully reactivated.", reactivateUser.UserNameOrEmailAddress);
            return GenericResponse<string>.Success("Operation Successful.", "User profile successfully reactivated.", HttpStatusCode.OK);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while reactivating user profile.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while deactivating user profile. Kindly retry.", HttpStatusCode.InternalServerError);
        }

    }
}
