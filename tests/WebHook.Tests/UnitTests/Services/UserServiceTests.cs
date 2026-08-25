using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class UserServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    //Fields
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;

    private readonly PostgreSqlFixture _postgreSqlFixture;
    private ServiceProvider _serviceProvider = null;

    // JWT secret must be set as env var — mirrors production requirement
    private const string JwtSecretEnvVar = "webhook_secret_key";
    private const string JwtSecret = "super-secret-key-for-testing-only-32chars!!";
    private const string DefaultPassword = "Test@1234!";
    private const string DefaultUserRole = "USER";

    public UserServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                        .Options;

        _postgreSqlFixture = postgreSqlFixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();

        _userManagerMock = CreateUserManagerMock();
    }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(JwtSecretEnvVar, JwtSecret);

        var services = new ServiceCollection();

        services.Configure<JwtSettingsConfiguration>(opts =>
        {
            opts.ValidIssuer = "webhook_service";
            opts.RefreshTokenExpirationAfterInSeconds = 3600;
            opts.TokenExpirationAfterInSeconds = 1800;
        });

        //Add databse test container service.
        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddLogging();

        services.AddIdentity<User, Role>(opts =>
        {
            //Password setttings configuration
            opts.Password.RequireDigit = true;
            opts.Password.RequireLowercase = true;
            opts.Password.RequireUppercase = true;
            opts.Password.RequireNonAlphanumeric = true;
            opts.Password.RequiredLength = 10;

            //User configuration
            opts.User.RequireUniqueEmail = true;


            //Signin configuration for user
            opts.SignIn.RequireConfirmedEmail = false;
            opts.SignIn.RequireConfirmedAccount = false;
            opts.SignIn.RequireConfirmedPhoneNumber = false;

            //Lockout settings configuration
            opts.Lockout.MaxFailedAccessAttempts = 3;
            opts.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromDays(36525); //Maximum possible timespan. This ensures users are locked ou indefinitely 
            opts.Lockout.AllowedForNewUsers = true; //This ensures that new users are created with lockout enabled.

        }).AddEntityFrameworkStores<RepositoryContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUserService, UserService>();
        services.AddHttpContextAccessor();


        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        if (!await roleManager.RoleExistsAsync(DefaultUserRole))
        {
            await roleManager.CreateAsync(new Role() { Name = DefaultUserRole, Description = "this is for base users." });
        }
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            Environment.SetEnvironmentVariable(JwtSecretEnvVar, null);
            await _serviceProvider.DisposeAsync();
        }

    }

    //Helper methods
    private CreateUserDto BuildUserToCreate(string emailAddress = "test@mail.com", string password = DefaultPassword, string firstName = "John", string lastName = "Doe", string username = "testUser112") => new CreateUserDto()
    {
        EmailAddress = emailAddress,
        Password = password,
        ConfirmPassword = password,
        FirstName = firstName,
        LastName = lastName,
        UserName = username
    };

    private UserService CreateSut()
    {
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var userManager = _serviceProvider.GetRequiredService<UserManager<User>>();
        return new UserService(ctx, userManager);
    }

    private async Task<(string email, string userName)> SeedUserAsync(string emailAddress = "test@mail.com", string username = "testUser112", string? password = DefaultPassword)
    {
        var sut = CreateSut();
        var dto = BuildUserToCreate(emailAddress: emailAddress, username: username, password: password);
        var result = await sut.CreateUserAsync(dto);

        Assert.True(result.IsSuccessful, $"Seed user failed: {result.ResponseMessage}");
        return (dto.EmailAddress, dto.UserName);
    }

    private UserDeactivationRequestDto BuildUserDeactivateRequest(string usernameoremail = "test@mail.com", string justification = "This is for test purpose.") => new UserDeactivationRequestDto() { UserNameOrEmailAddress = usernameoremail, DeactivationJustification = justification };

    private ReactivateUserRequestDto BuildUserReactivationRequest(string usernameoremail = "test@mail.com") => new ReactivateUserRequestDto() { UserNameOrEmailAddress = usernameoremail };

    private UserService GetSut()
    {
        return new UserService(new RepositoryContext(_dbContextOptions), _userManagerMock.Object);
    }

    private Mock<UserManager<User>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<User>>();

        return new Mock<UserManager<User>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new Mock<IPasswordHasher<User>>().Object,
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new Mock<ILookupNormalizer>().Object,
            new IdentityErrorDescriber(),
            new Mock<IServiceProvider>().Object,
            new Mock<ILogger<UserManager<User>>>().Object);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_Returns201Created()
    {
        //Arrange
        var sut = CreateSut();
        var request = BuildUserToCreate();

        //Act
        var result = await sut.CreateUserAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User created successfully.", result.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var createdUser = await userManager.FindByEmailAsync(request.EmailAddress);

        Assert.NotNull(createdUser);
        Assert.True(createdUser.IsActive);
        Assert.Equal(request.EmailAddress, createdUser.NormalizedEmail, ignoreCase: true);
        Assert.True(createdUser.LockoutEnabled);
    }

    [Fact]
    public async Task CreateAsync_InvalidRequest_RoleNotExist_Returns201Created()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
        var existingRole = await roleManager.FindByNameAsync("user");

        if (existingRole is not null)
        {
            var deletRoleResult = await roleManager.DeleteAsync(existingRole);
        }

        var userToCreate = BuildUserToCreate();
        var sut = CreateSut();


        //Act
        var result = await sut.CreateUserAsync(userToCreate);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User created.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var createdUser = await userManager.FindByEmailAsync(userToCreate.EmailAddress);
        Assert.NotNull(createdUser);
        var isRoleMaintained = await userManager.GetRolesAsync(createdUser);
        Assert.Empty(isRoleMaintained);
    }

    [Fact]
    public async Task CreateAsync_EmailExists_Returns409Conflict()
    {
        //Arrange
        var seedResult = await SeedUserAsync();

        var sut = CreateSut();
        var userToCreate = BuildUserToCreate();

        //Act
        var result = await sut.CreateUserAsync(userToCreate);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with email alraady exists.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_UsernameCntainsInvalidCharacter_Returns409Conflict()
    {
        //Arrange
        var seedResult = await SeedUserAsync();

        var sut = CreateSut();
        var userToCreate = BuildUserToCreate(username: "tested@001");

        //Act
        var result = await sut.CreateUserAsync(userToCreate);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Username cannot contan the special character: @", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_UsernameExists_Returns409Conflict()
    {
        //Arrange
        var seedResult = await SeedUserAsync();

        var sut = CreateSut();
        var userToCreate = BuildUserToCreate(emailAddress: "userexample@mail.com");

        //Act
        var result = await sut.CreateUserAsync(userToCreate);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Username already taken.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreationSuccessful()
    {
        //Arrange
        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.FindByNameAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), "USER"))
            .ReturnsAsync(IdentityResult.Success);

        var sut = GetSut();

        //Act
        var result = await sut.CreateUserAsync(request, CancellationToken.None);

        //Asseert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User created successfully.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_InValidRequest_RolesNotAdded_CreationSuccessful()
    {
        //Arrange
        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.FindByNameAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Success);

        var identityErrors = new[]
        {
            new IdentityError
            {
                Code = "Role does not exist",
                Description = "Invalid Role."
            }
        };

        _userManagerMock.Setup(x => x.AddToRoleAsync(It.IsAny<User>(), "USER"))
            .ReturnsAsync(IdentityResult.Failed(identityErrors));

        var sut = GetSut();

        //Act
        var result = await sut.CreateUserAsync(request, CancellationToken.None);

        //Asseert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User created.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateUserAsync_EmailExist_ReturnsConflict()
    {
        //Arrange

        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        var existingUser = new User() { Email = request.EmailAddress };

        _userManagerMock.Setup(x => x.FindByEmailAsync(existingUser.Email))
            .ReturnsAsync(existingUser);

        var sut = GetSut();

        //Act
        var result = await sut.CreateUserAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with email alraady exists.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateUserAsync_UsernameExist_ReturnsConflict()
    {
        //Arrange

        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        var existingUser = new User() { Email = request.EmailAddress, UserName = "johndoe" };

        _userManagerMock.Setup(x => x.FindByNameAsync(existingUser.UserName))
            .ReturnsAsync(existingUser);

        var sut = GetSut();

        //Act
        var result = await sut.CreateUserAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Username already taken.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_ValidRequest_CreationUnsuccessful_ReturnsBadRequest()
    {
        //Arrange
        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        _userManagerMock.Setup(x => x.FindByNameAsync(request.EmailAddress))
            .ReturnsAsync((User?)null);

        var identityError = new IdentityError[]
        {
            new IdentityError(){ Code = "09", Description = "User creation failed" }
        };

        _userManagerMock.Setup(x => x.CreateAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        var sut = GetSut();

        //Act
        var result = await sut.CreateUserAsync(request, CancellationToken.None);

        //Asseert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Your profile could not be created. Kindly retry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task CreateAsync_CancellationRequested_ReturnsInternalServerError()
    {
        //Arrange
        var request = new CreateUserDto
        {
            FirstName = "John",
            LastName = "Doe",
            UserName = "johndoe",
            EmailAddress = "john@example.com",
            Password = "Password123!",
            ConfirmPassword = "Password123!"
        };

        var sut = GetSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.CreateUserAsync(request, cts.Token);

        //Asseert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("An error occurred while creating user.", result.ResponseMessage, ignoreCase: true);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_ValidRequest_EmailAddressProvided_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var userDeactivateRequest = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        //Act
        var result = await sut.DeactivateUserProfileAsync(userDeactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);

        using var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NormalizedEmail.Contains(userDeactivateRequest.UserNameOrEmailAddress.ToUpper()));
        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(userDeactivateRequest.DeactivationJustification, deactivatedUser.DeactivationJustification, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_ValidRequest_UsernameProvided_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var userDeactivateRequest = BuildUserDeactivateRequest(usernameoremail: seedResult.userName);
        var sut = CreateSut();

        //Act
        var result = await sut.DeactivateUserProfileAsync(userDeactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);

        using var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.NormalizedUserName.Contains(userDeactivateRequest.UserNameOrEmailAddress.ToUpper()));
        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(userDeactivateRequest.DeactivationJustification, deactivatedUser.DeactivationJustification, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_EmailNotExist_Returns404NotFound()
    {
        //Arrange
        var userDeactivateRequest = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        //Act
        var result = await sut.DeactivateUserProfileAsync(userDeactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("User with details does not exist -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(userDeactivateRequest.UserNameOrEmailAddress, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_UsernameNotExist_Returns404NotFound()
    {
        //Arrange
        var userDeactivateRequest = BuildUserDeactivateRequest(usernameoremail: "testuser101");
        var sut = CreateSut();

        //Act
        var result = await sut.DeactivateUserProfileAsync(userDeactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("User with details does not exist -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(userDeactivateRequest.UserNameOrEmailAddress, result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_ValidEmail_UserActive_Returns409Conflict()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var reactivateRequest = BuildUserReactivationRequest(seedUserResult.email);
        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateUserProfileAsync(reactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile is currently active.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);

        using var scope = _serviceProvider.CreateScope();
        var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var userFromDb = await usermanager.FindByEmailAsync(reactivateRequest.UserNameOrEmailAddress);

        Assert.NotNull(userFromDb);
        Assert.True(userFromDb.IsActive);
        Assert.Null(userFromDb.DeletedAt);
        Assert.Null(userFromDb.DeactivationJustification);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_ValidUsername_UserActive_Returns409Conflict()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var reactivateRequest = BuildUserReactivationRequest(seedUserResult.userName);
        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateUserProfileAsync(reactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile is currently active.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);

        using var scope = _serviceProvider.CreateScope();
        var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var userFromDb = await usermanager.FindByNameAsync(reactivateRequest.UserNameOrEmailAddress);

        Assert.NotNull(userFromDb);
        Assert.True(userFromDb.IsActive);
        Assert.Null(userFromDb.DeletedAt);
        Assert.Null(userFromDb.DeactivationJustification);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_ValidEmail_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var deactivateRequest = BuildUserDeactivateRequest(usernameoremail: seedResult.email);

        using (var scope = _serviceProvider.CreateScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
            var deactivateResult = await userService.DeactivateUserProfileAsync(deactivateRequest);
            Assert.NotNull(deactivateResult);
            Assert.True(deactivateResult.IsSuccessful);
        }

        var reactivateRequest = BuildUserReactivationRequest(usernameoremail: seedResult.email);
        //var sut = CreateSut();

        //Act
        using (var reactivationScope = _serviceProvider.CreateScope())
        {
            var userService = reactivationScope.ServiceProvider.GetRequiredService<IUserService>();
            var result = await userService.ReactivateUserProfileAsync(reactivateRequest);

            //Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccessful, result.ResponseMessage);
            Assert.NotNull(result.ResponseData);
            Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
            Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
            Assert.Equal("User profile successfully reactivated.", result.ResponseMessage, ignoreCase: true);
        }




        var assertScope = _serviceProvider.CreateScope();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var deactivatedUser = await usermanager.FindByEmailAsync(reactivateRequest.UserNameOrEmailAddress);

        Assert.NotNull(deactivatedUser);
        Assert.True(deactivatedUser.IsActive);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_ValidUsername_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var deactivateRequest = BuildUserDeactivateRequest(usernameoremail: seedResult.userName);

        using (var scope = _serviceProvider.CreateScope())
        {
            var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
            var deactivateResult = await userService.DeactivateUserProfileAsync(deactivateRequest);
            Assert.NotNull(deactivateResult);
            Assert.True(deactivateResult.IsSuccessful);
        }



        var reactivateRequset = BuildUserReactivationRequest(usernameoremail: seedResult.userName);
        //var sut = CreateSut();

        //Act
        using (var reactivationScope = _serviceProvider.CreateScope())
        {
            var reactivationUserService = reactivationScope.ServiceProvider.GetRequiredService<IUserService>();

            var result = await reactivationUserService.ReactivateUserProfileAsync(reactivateRequset, CancellationToken.None);

            //Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuccessful, result.ResponseMessage);
            Assert.NotNull(result.ResponseData);
            Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
            Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
            Assert.Equal("User profile successfully reactivated.", result.ResponseMessage, ignoreCase: true);
        }


        var assertScope = _serviceProvider.CreateScope();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var deactivatedUser = await usermanager.FindByNameAsync(reactivateRequset.UserNameOrEmailAddress);

        Assert.NotNull(deactivatedUser);
        Assert.True(deactivatedUser.IsActive);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_EmailNotExist_Returns404NotFound()
    {
        //Arrange
        var userReactivateRequest = BuildUserReactivationRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateUserProfileAsync(userReactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with provided details does not exist.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_UsernameNotExist_Returns404NotFound()
    {
        //Arrange
        var userReactivateRequest = BuildUserReactivationRequest(usernameoremail: "testuser101");
        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateUserProfileAsync(userReactivateRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with provided details does not exist.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ReactivateUserProfileAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var reactivationRequest = BuildUserReactivationRequest();
        var sut = CreateSut();

        //Act
        var result = await sut.ReactivateUserProfileAsync(reactivationRequest, cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("An error occurred while deactivating user profile. Kindly retry.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
    }
}
