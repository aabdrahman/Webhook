using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class AuthenticationServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signinManagerMock;
    private readonly Mock<IOptionsMonitor<JwtSettingsConfiguration>> _settingsConfigMock;
    private readonly Mock<IOtpGenerator> _otpGeneratorMock;
    private readonly Mock<IApplicationHasher> _applicationHasherMock;
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly string _tempDirectory;
    private readonly string _templateDirectory;

    private ServiceProvider _serviceProvider = null;

    private const string JwtSecretEnvVar = "webhook_secret_key";
    private const string JwtSecret = "super-secret-key-for-testing-only-32chars!!";
    private const string DefaultPassword = "Test@1234!";
    private const string DefaultUserRole = "USER";

    public AuthenticationServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                        .Options;

        _postgreSqlFixture = postgreSqlFixture;

        _userManagerMock = CreateUserManagerMock();
        _signinManagerMock = CreateSignInManagerMock(_userManagerMock);
        _settingsConfigMock = new Mock<IOptionsMonitor<JwtSettingsConfiguration>>();
        _otpGeneratorMock = new Mock<IOtpGenerator>();
        _applicationHasherMock = new Mock<IApplicationHasher>();

        // Create a real temp directory so File.Exists and File.ReadAllTextAsync work
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _templateDirectory = Path.Combine(_tempDirectory, "EmailNotificationTemplates");

        Directory.CreateDirectory(_templateDirectory);

        File.WriteAllText(
            Path.Combine(_templateDirectory, "DeadLetterNotification.html"),
            "<p>Dear {{ContactName}}, delivery {{DeliveryId}} failed.</p>");

        File.WriteAllText(
            Path.Combine(_templateDirectory, "SlowEndpointNotification.html"),
            "<p>{{SubscriptionName}} took {{ResponseTimeMs}}ms</p>");

    }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable(JwtSecretEnvVar, JwtSecret);
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(connectionString: _postgreSqlFixture.ConnectionString);
        });

        services.AddSingleton(_ =>
        {
            return Channel.CreateUnbounded<EventRaised>(new UnboundedChannelOptions()
            {
                SingleReader = true,
                SingleWriter = false

            });
        });

        var environment = new TestWebHostEnvironment
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new PhysicalFileProvider(AppContext.BaseDirectory)
        };

        services.AddSingleton<IWebHostEnvironment>(environment);

        services.Configure<JwtSettingsConfiguration>(opts =>
        {
            opts.ValidIssuer = "";
            opts.ValidAudiences = "";
            opts.RefreshTokenExpirationAfterInSeconds = 3600;
            opts.TokenExpirationAfterInSeconds = 1800;
        });

        services.Configure<OtpSettingsConfiguration>(opts =>
        {
            opts.OtpToGenerateLength = 6;
            opts.MaximumOtpLength = 12;
        });

        services.Configure<TokenValidationConfiguration>(opts =>
        {
            opts.OtpExpirationAfterInSeconds = 1200;
            opts.OtpOperationTokenExpiresAFterInSceonds = 2400;
        });

        services.AddLogging();
        services.AddHttpContextAccessor();
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
        services.AddScoped<Core.Interfaces.Services.IAuthenticationService, Infrastructure.Services.AuthenticationService>();
        services.AddScoped<IOtpGenerator, OtpGenerator>();
        services.AddScoped<IApplicationHasher, ApplicationHasher>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<EmailContentFormatterHelper>();

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
        Environment.SetEnvironmentVariable(JwtSecretEnvVar, null);

        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    private CreateUserDto BuildUserToCreate(string emailAddress = "test@mail.com", string password = DefaultPassword, string firstName = "John", string lastName = "Doe", string username = "testUser112") => new CreateUserDto()
    {
        EmailAddress = emailAddress,
        Password = password,
        ConfirmPassword = password,
        FirstName = firstName,
        LastName = lastName,
        UserName = username
    };

    private ChangePasswordDto BuildPasswordChangeRequest(string emailAddress = "test@mail.com", string oldPassword = DefaultPassword, string newPassword = "NewPassword@12345") => new ChangePasswordDto()
    {
        UserNameOrEmailAddress = emailAddress,
        OldPassword = oldPassword,
        NewPassword = newPassword,
        ConfirmNewPassword = newPassword
    };

    private Infrastructure.Services.AuthenticationService CreateSut(IOtpGenerator otpGenerator = null, IApplicationHasher applicationHasher = null)
    {
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        return new Infrastructure.Services.AuthenticationService(
            _serviceProvider.GetRequiredService<UserManager<User>>(), ctx,
            _serviceProvider.GetRequiredService<IOptionsMonitor<JwtSettingsConfiguration>>(), _serviceProvider.GetRequiredService<SignInManager<User>>(),
            otpGenerator ?? _serviceProvider.GetRequiredService<IOtpGenerator>(), _serviceProvider.GetRequiredService<IOptionsMonitor<OtpSettingsConfiguration>>(), _serviceProvider.GetRequiredService<IOptionsMonitor<TokenValidationConfiguration>>(),
            applicationHasher ?? _serviceProvider.GetRequiredService<IApplicationHasher>(), _serviceProvider.GetRequiredService<IEmailService>(), _serviceProvider.GetRequiredService<EmailContentFormatterHelper>()
            );
    }

    private RequestOtpDto BuildOtpRequest(string usernameoremail = "test@mail.com", OtpPurpose purpose = OtpPurpose.PasswordReset) => new RequestOtpDto() { Purpose = purpose, UserNameOrEmailAddress = usernameoremail };

    private LoginUserDto BuildLoginEntity(string usernameoremail = "", string password = DefaultPassword)
    {
        return new LoginUserDto() { UserNameOrEmailAddress = usernameoremail, Password = password };
    }

    private async Task<(string email, string userName)> SeedUserAsync(string emailAddress = "test@mail.com", string username = "testUser112", string? password = DefaultPassword)
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var dto = BuildUserToCreate(emailAddress: emailAddress, username: username, password: password);
        var result = await userService.CreateUserAsync(dto);

        Assert.True(result.IsSuccessful, $"Seed user failed: {result.ResponseMessage}");
        return (dto.EmailAddress, dto.UserName);
    }

    //private Infrastructure.Services.AuthenticationService GetSut()
    //{
    //    return new Infrastructure.Services.AuthenticationService(_userManagerMock.Object, new RepositoryContext(_dbContextOptions), _settingsConfigMock.Object, _signinManagerMock.Object);
    //}

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

    private Mock<SignInManager<User>> CreateSignInManagerMock(
    Mock<UserManager<User>> userManagerMock)
    {
        var contextAccessor = new Mock<IHttpContextAccessor>();
        var claimsFactory = new Mock<IUserClaimsPrincipalFactory<User>>();
        var options = new Mock<IOptions<IdentityOptions>>();
        var logger = new Mock<ILogger<SignInManager<User>>>();
        var schemes = new Mock<IAuthenticationSchemeProvider>();
        var confirmation = new Mock<IUserConfirmation<User>>();

        return new Mock<SignInManager<User>>(
            userManagerMock.Object,
            contextAccessor.Object,
            claimsFactory.Object,
            options.Object,
            logger.Object,
            schemes.Object,
            confirmation.Object);
    }

    [Fact]
    public async Task LoginUserAsync_ValidRequest_UserEmail_Returns200OK()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "test@mail.com");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("User signed in successfully.", result.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggedInUser = await userManager.FindByEmailAsync(loginRequest.UserNameOrEmailAddress);

        Assert.NotNull(loggedInUser);
        Assert.NotNull(loggedInUser.LastLoginDate);
        Assert.NotNull(loggedInUser.LastAuthenticatedAt);
        Assert.NotNull(loggedInUser.RefreshToken);
        Assert.NotNull(loggedInUser.TokenExpirationTime);
        Assert.True(loggedInUser.LastLoginDate.Value == loggedInUser.LastAuthenticatedAt.Value);
        Assert.True(loggedInUser.TokenExpirationTime > loggedInUser.LastAuthenticatedAt);
    }

    [Fact]
    public async Task LoginUserAsync_ValidRequest_UserName_Returns200OK()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "testUser112");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful, result.ResponseMessage);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("User signed in successfully.", result.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggedInUser = await userManager.FindByNameAsync(loginRequest.UserNameOrEmailAddress);

        Assert.NotNull(loggedInUser);
        Assert.NotNull(loggedInUser.LastLoginDate);
        Assert.NotNull(loggedInUser.LastAuthenticatedAt);
        Assert.NotNull(loggedInUser.RefreshToken);
        Assert.NotNull(loggedInUser.TokenExpirationTime);
        Assert.True(loggedInUser.LastLoginDate.Value == loggedInUser.LastAuthenticatedAt.Value);
        Assert.True(loggedInUser.TokenExpirationTime > loggedInUser.LastAuthenticatedAt);
    }

    [Fact]
    public async Task LoginUserAsync_InvalidRequest_ValidUsername_InvalidPassword_Returns400BadRequest()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "testUser112", password: "Password@1234!");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful, result.ResponseMessage);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggedInUser = await userManager.FindByNameAsync(loginRequest.UserNameOrEmailAddress);

        Assert.NotNull(loggedInUser);
        Assert.Null(loggedInUser.LastLoginDate);
        Assert.Null(loggedInUser.LastAuthenticatedAt);
        Assert.Null(loggedInUser.RefreshToken);
        Assert.Null(loggedInUser.TokenExpirationTime);
    }

    [Fact]
    public async Task LoginUserAsync_InvalidRequest_ValidEmailAddress_InvalidPassword_Returns400BadRequest()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "test@mail.com", password: "Password@1234!");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful, result.ResponseMessage);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggedInUser = await userManager.FindByEmailAsync(loginRequest.UserNameOrEmailAddress);

        Assert.NotNull(loggedInUser);
        Assert.Null(loggedInUser.LastLoginDate);
        Assert.Null(loggedInUser.LastAuthenticatedAt);
        Assert.Null(loggedInUser.RefreshToken);
        Assert.Null(loggedInUser.TokenExpirationTime);
    }

    [Fact]
    public async Task LoginUserAsync_InvalidRequest_InValidEmailAddress_ValidPassword_Returns404NotFound()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "test@mail2.com");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful, result.ResponseMessage);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        //var assertScope = _serviceProvider.CreateScope();
        //var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        //var loggedInUser = await userManager.FindByNameAsync(loginRequest.UserNameOrEmailAddress);

        //Assert.NotNull(loggedInUser);
        //Assert.Null(loggedInUser.LastLoginDate);
        //Assert.Null(loggedInUser.LastAuthenticatedAt);
        //Assert.Null(loggedInUser.RefreshToken);
        //Assert.Null(loggedInUser.TokenExpirationTime);
    }

    [Fact]
    public async Task LoginUserAsync_InvalidRequest_InValidUsername_ValidPassword_Returns404NotFound()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "testedUser112");

        var sut = CreateSut();

        //Act
        var result = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful, result.ResponseMessage);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        //var assertScope = _serviceProvider.CreateScope();
        //var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        //var loggedInUser = await userManager.FindByNameAsync(loginRequest.UserNameOrEmailAddress);

        //Assert.NotNull(loggedInUser);
        //Assert.Null(loggedInUser.LastLoginDate);
        //Assert.Null(loggedInUser.LastAuthenticatedAt);
        //Assert.Null(loggedInUser.RefreshToken);
        //Assert.Null(loggedInUser.TokenExpirationTime);
    }

    [Fact]
    public async Task LoginUserAsync_MaxAttemptExceeded_UserLockedOut()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        var loginRequest = BuildLoginEntity(usernameoremail: "testUser112", password: "Password@123456");

        var sut = CreateSut();

        //Act
        for (int i = 0; i < 3; i++)
        {
            await Task.Delay(1000);
            var result = await sut.LoginUserAsync(loginRequest);
            Assert.NotNull(result);
            Assert.False(result.IsSuccessful);
            Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        }

        loginRequest.Password = DefaultPassword;
        var lockedOutResult = await sut.LoginUserAsync(loginRequest);

        //Assert
        Assert.NotNull(lockedOutResult);
        Assert.False(lockedOutResult.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, lockedOutResult.HttpStatusCode);
        Assert.Equal("User profiled locked out. Kindly contact admin or reset your password.", lockedOutResult.ResponseMessage, ignoreCase: true);

        var assertScope = _serviceProvider.CreateScope();
        var userManager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var loggedInUser = await userManager.FindByNameAsync(loginRequest.UserNameOrEmailAddress);

        Assert.NotNull(loggedInUser);
        Assert.Null(loggedInUser.LastLoginDate);
        Assert.Null(loggedInUser.LastAuthenticatedAt);
        Assert.Null(loggedInUser.RefreshToken);
        Assert.Null(loggedInUser.TokenExpirationTime);
        Assert.NotNull(loggedInUser.LockoutEnd);
        Assert.True(loggedInUser.LockoutEnd.Value > DateTimeOffset.UtcNow);
    }


    [Fact]
    public async Task ChangePasswordAsync_EmailNotExist_Returns400BadRequest()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.ChangePasswordAsync(BuildPasswordChangeRequest(emailAddress: "usereamil@exampl.com"));

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ChangePasswordAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var changePasswordRequest = BuildPasswordChangeRequest(emailAddress: seedResult.email);
        var sut = CreateSut();

        //Act
        var result = await sut.ChangePasswordAsync(changePasswordRequest, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Password updated successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        User? modifiedUser = await usermanager.FindByEmailAsync(changePasswordRequest.UserNameOrEmailAddress);
        Assert.NotNull(modifiedUser);
        bool isPasswordChanged = await usermanager.CheckPasswordAsync(modifiedUser, changePasswordRequest.NewPassword);
        Assert.True(isPasswordChanged, "Password not changed.");
    }

    [Fact]
    public async Task ChangePasswordAsync_InvalidRequest_IncorrectOldPassword_Returns400BadRequest()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var sut = CreateSut();
        var changePasswordRequest = BuildPasswordChangeRequest(emailAddress: seedResult.email, oldPassword: "Tested@1290");

        //Act
        var result = await sut.ChangePasswordAsync(changePasswordRequest, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();

        User? modifiedUser = await usermanager.FindByEmailAsync(changePasswordRequest.UserNameOrEmailAddress);
        Assert.NotNull(modifiedUser);
        bool isPasswordChanged = await usermanager.CheckPasswordAsync(modifiedUser, DefaultPassword);
        Assert.True(isPasswordChanged, "Password was changed.");
    }

    [Fact]
    public async Task RequestOtpAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var request = BuildOtpRequest();
        var sut = CreateSut();

        //Act
        var result = await sut.RequestOtpAsync(ct: cts.Token, requestOtp: request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("An error occurred requesting for OTP. Kindly retry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RequestOtpAsync_ValidRequest_UserEmail_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("OTP sent successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var otpConfig = assertScope.ServiceProvider.GetRequiredService<IOptionsMonitor<TokenValidationConfiguration>>();

        var user = await usermanager.FindByEmailAsync(request.UserNameOrEmailAddress);
        Assert.NotNull(user);
        List<OtpVerification> otps = await assertCtx.OtpVerifications.Where(x => x.UserId == user.Id).ToListAsync();
        Assert.Single(otps);
        var userOtp = otps.First();
        Assert.True(userOtp.CreatedAt < userOtp.ExpiresAt);
        Assert.Equal(otpConfig.CurrentValue.OtpExpirationAfterInSeconds, (userOtp.ExpiresAt - userOtp.CreatedAt).TotalSeconds, tolerance: 5);
    }

    [Fact]
    public async Task RequestOtpAsync_ValidRequest_UserName_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(usernameoremail: seedResult.userName);
        var sut = CreateSut();

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("OTP sent successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var usermanager = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var otpConfig = assertScope.ServiceProvider.GetRequiredService<IOptionsMonitor<TokenValidationConfiguration>>();

        var user = await usermanager.FindByNameAsync(request.UserNameOrEmailAddress);
        Assert.NotNull(user);
        List<OtpVerification> otps = await assertCtx.OtpVerifications.Where(x => x.UserId == user.Id).ToListAsync();
        Assert.Single(otps);
        var userOtp = otps.First();
        Assert.True(userOtp.CreatedAt < userOtp.ExpiresAt);
        Assert.Equal(otpConfig.CurrentValue.OtpExpirationAfterInSeconds, (userOtp.ExpiresAt - userOtp.CreatedAt).TotalSeconds, tolerance: 5);
    }

    [Fact]
    public async Task RequestOtpAsync_InValidRequest_UserNameNotExist_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(usernameoremail: "testeduser123");
        var sut = CreateSut();

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with details does not exist.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RequestOtpAsync_InValidRequest_EmailNotExist_Returns200OK()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(usernameoremail: "testuser@mail.com");
        var sut = CreateSut();

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User with details does not exist.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RequestOtpAsync_ValidRequest_OtpGenerationFailedReturnsFailed()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(seedResult.email);

        _otpGeneratorMock.Setup(x => x.GenerateOtp(It.IsAny<int>(), It.IsAny<int>())).Returns("");
        _applicationHasherMock.Setup(x => x.HashSecret(It.IsAny<string>())).ReturnsAsync("hashed-secret");
        var sut = CreateSut(otpGenerator: _otpGeneratorMock.Object, applicationHasher: _applicationHasherMock.Object);

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(request);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Operation could not be completed. Kindly retry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RequestOtpAsync_ValidRequest_HashingFailedReturnsFailed()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var request = BuildOtpRequest(seedResult.email);

        _otpGeneratorMock.Setup(x => x.GenerateOtp(It.IsAny<int>(), It.IsAny<int>())).Returns("123456");
        _applicationHasherMock.Setup(x => x.HashSecret(It.IsAny<string>())).ReturnsAsync("");
        var sut = CreateSut(otpGenerator: _otpGeneratorMock.Object, applicationHasher: _applicationHasherMock.Object);

        //Act
        var result = await sut.RequestOtpAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(request);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.IsSuccessful);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Operation could not be completed. Kindly retry.", result.ResponseMessage, ignoreCase: true);
    }
}
