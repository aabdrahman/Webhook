using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Core.Mapper;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.Services;

public sealed class UserService : IUserService
{
    private readonly UserManager<User> _userManager;
    private readonly RepositoryContext _repositoryContext;

    public UserService(RepositoryContext repositoryContext, UserManager<User> userManager)
    {
        _repositoryContext = repositoryContext;
        _userManager = userManager;

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

            User? userToDeactivate = userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ?
                                        await _userManager.FindByEmailAsync(userDeactivationRequest.UserNameOrEmailAddress) :
                                        await _userManager.FindByNameAsync(userDeactivationRequest.UserNameOrEmailAddress);

            if(userToDeactivate is null)
            {
                _logger.Warning(userDeactivationRequest.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase) ? "User with email does not exist - {0}" : "User with provided user name does not exist - {0}", userDeactivationRequest.UserNameOrEmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", $"User with details does not exist - {userDeactivationRequest.UserNameOrEmailAddress}", HttpStatusCode.NotFound);
            }

            userToDeactivate.IsActive = false;
            userToDeactivate.DeactivationJustification = userDeactivationRequest.DeactivationJustification;
            userToDeactivate.DeletedAt = DateTimeOffset.UtcNow;
            userToDeactivate.DeletedByUserId = "";
            IdentityResult deactivateResult = await _userManager.UpdateAsync(userToDeactivate);

            if (!deactivateResult.Succeeded)
            {
                _logger.Warning("User profile could not be deactivated. Errors - {0}", deactivateResult.Errors.ToList());
                return GenericResponse<string>.Failure("Operation Failed.", "User profile could not be deactivated. Kindly retry.", HttpStatusCode.BadRequest);
            }

            _logger.Information("User profile successfully deactivated - {0}", userDeactivationRequest.UserNameOrEmailAddress);
            return GenericResponse<string>.Success("Operation Successful.", "User profile successfully deactivated.", HttpStatusCode.OK);

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

            if(reactivateUser.UserNameOrEmailAddress.Contains("@", StringComparison.OrdinalIgnoreCase))
            {
                userToReactivateQuery = userToReactivateQuery.Where(x => x.NormalizedEmail.Contains(reactivateUser.UserNameOrEmailAddress.ToUpper()));
            }
            else
            {
                userToReactivateQuery = userToReactivateQuery.Where(x => x.NormalizedUserName.Contains(reactivateUser.UserNameOrEmailAddress.ToUpper()));
            }

            User? userToReactivate = await userToReactivateQuery.FirstOrDefaultAsync(ct);

            if(userToReactivate is null)
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
