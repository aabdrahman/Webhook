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

internal class UserService : IUserService
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
            var isUserExists = await _repositoryContext.Users.AnyAsync(x => x.NormalizedEmail == createUser.EmailAddress.ToUpper() || x.NormalizedUserName == createUser.UserName.ToUpper(), ct);

            var validationResult = await _repositoryContext.Users.Select(x => new
            {
                UserNameExists =  _repositoryContext.Users.Any(x => x.NormalizedUserName == createUser.UserName.ToUpper()),
                EmailExists = _repositoryContext.Users.Any(x => x.NormalizedEmail == createUser.EmailAddress.ToUpper())
            }).FirstOrDefaultAsync(ct);

            if (validationResult is not null && validationResult.EmailExists)
            {
                _logger.Warning("User with email already exists - {0}", createUser.EmailAddress);
                return GenericResponse<string>.Failure("Operation Failed.", "User with email alraady exists.", HttpStatusCode.Conflict);
            }

            if(validationResult is not null && validationResult.UserNameExists)
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
            IdentityResult setRolesResult = await _userManager.AddToRoleAsync(userToCreate, "USER");

            if (!setRolesResult.Succeeded)
            {
                _logger.Warning("Role could not be added to the created user.", setRolesResult.Errors.ToList());
                return GenericResponse<string>.Success("Operation Successful", "User created.", HttpStatusCode.Created);
            }

            //Roles successfully created for the user. returning success response.
            _logger.Information("User created successfully an droles successfully maintained for user");
            return GenericResponse<string>.Success("Operation Successful", "User created successfully.", HttpStatusCode.Created);

        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while creating user.");
            return GenericResponse<string>.Failure("Operation Failed.", "An error occurred while creating user..", HttpStatusCode.InternalServerError, 
                new ErrorDetail() { ErrorTitle = ex.GetType().Name, ErrorMessage = ex.Message, ErrorDescription = ex.InnerException?.Message ?? "" });
        }
    }
}
