using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.DataTransferObjects.OtpOperation;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class UserServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    //Fields
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private ServiceProvider _serviceProvider = null!;

    // Reassigned per test via SetAuthenticatedUser() — must not be readonly
    private Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock = new();

    // Reassigned per test via SetDataProtectorToThrow/Return — null means use real protector
    private Mock<IDataProtectionProvider>? _dataProtectionProviderMock;

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
        services.AddDataProtection();

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

        services.AddScoped<IApplicationHasher, ApplicationHasher>();
        services.AddScoped<IAuthenticatedUserDetails, AuthenticatedUserDetails>();
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
        var applicationHasher = _serviceProvider.GetRequiredService<IApplicationHasher>();

        // Use the mock protector when a test has configured one, otherwise use the
        // real IDataProtectionProvider registered in the service collection
        IDataProtectionProvider dataProtectionProvider = _dataProtectionProviderMock is not null
            ? _dataProtectionProviderMock.Object
            : _serviceProvider.GetRequiredService<IDataProtectionProvider>();

        return new UserService(
            ctx,
            userManager,
            _authenticatedUserDetailsMock.Object,
            dataProtectionProvider,
            applicationHasher);
    }

    private static OtpVerificationSigning BuildOtpVerificationSigning(string? jti = null, string? issuedFor = null) => new()
    {
        Jti = jti ?? Guid.NewGuid().ToString(),
        IssuedFor = issuedFor ?? "TEST@MAIL.COM"
    };

    private async Task<(string email, string userName, string userId, string normalizedEmail)> SeedUserAsync(string emailAddress = "test@mail.com", string username = "testUser112", string? password = DefaultPassword)
    {
        // Seed using Admin context so no token validation is triggered
        SetAuthenticatedUser(role: "Admin");
        _dataProtectionProviderMock = null;

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var applicationHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

        // Use the mock protector when a test has configured one, otherwise use the
        // real IDataProtectionProvider registered in the service collection
        IDataProtectionProvider dataProtectionProvider = _dataProtectionProviderMock is not null
            ? _dataProtectionProviderMock.Object
            : scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();

        var sut = new UserService(ctx, userManager, _authenticatedUserDetailsMock.Object, dataProtectionProvider, applicationHasher);

        var dto = BuildUserToCreate(emailAddress: emailAddress, username: username, password: password ?? DefaultPassword);

        var result = await sut.CreateUserAsync(dto);
        Assert.True(result.IsSuccessful, $"Seed user failed: {result.ResponseMessage}");

        // Resolve the seeded user to retrieve their Id and NormalizedEmail
        using var assertScope = _serviceProvider.CreateScope();
        var userMgr = assertScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var seededUser = await userMgr.FindByEmailAsync(dto.EmailAddress);

        Assert.NotNull(seededUser);
        return (dto.EmailAddress, dto.UserName, seededUser!.Id.ToString("N"), seededUser.NormalizedEmail!);
    }

    private async Task<(Guid jti, string rawToken)> SeedOperationTokenAsync(string userId, OtpPurpose purpose = OtpPurpose.DeactivateProfile, bool consumed = false, bool revoked = false, DateTimeOffset? expiresAt = null)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();
        var dataProtectionProvider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var dataProtector = dataProtectionProvider.CreateProtector("Webhook.Otp.OtpVerificationSigning");

        string rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        string tokenHash = await hasher.HashSecret(rawToken);

        var jti = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var tokenExpiry = expiresAt ?? now.AddMinutes(10);

        var otpVerification = new OtpVerification
        {
            Id = Guid.NewGuid(),
            UserId = Guid.TryParse(userId, out var corrUserId) ? corrUserId : Guid.NewGuid(),
            CreatedAt = now,
            ValidatedAt = now, // already validated
            ConsumedAt = consumed ? now : null,
            IsConsumed = consumed
        };

        var operationToken = new OtpOperationToken
        {
            Id = Guid.NewGuid(),
            Jti = jti,
            TokenHash = tokenHash,
            Purpose = purpose,
            CreatedAt = now,
            ExpiresAt = tokenExpiry,
            ConsumedAt = consumed ? now : null,
            RevokedAt = revoked ? now : null,
            OtpVerificationId = otpVerification.Id,
            OtpVerification = otpVerification
        };

        ctx.OtpVerifications.Add(otpVerification);
        ctx.OtpOperationTokens.Add(operationToken);
        await ctx.SaveChangesAsync();

        return (jti, rawToken);
    }

    private void SetAuthenticatedUser(string role = "USER", string operationToken = "", string? userId = null)
    {
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();

        _authenticatedUserDetailsMock
            .Setup(x => x.assignedRole)
            .Returns(role);

        _authenticatedUserDetailsMock
            .Setup(x => x.operationToken)
            .Returns(operationToken);

        _authenticatedUserDetailsMock
            .Setup(x => x.userId)
            .Returns(userId ?? Guid.NewGuid().ToString());

        _authenticatedUserDetailsMock.Setup(x => x.emailAddress).Returns("test@mail.com".ToUpper());

        // Reset the protector mock so each test starts with the real protector
        // unless SetDataProtectorToThrow or SetDataProtectorToReturn is called after
        _dataProtectionProviderMock = null;
    }

    /// <summary>
    /// Configures a mock <see cref="IDataProtectionProvider"/> whose protector
    /// throws <see cref="CryptographicException"/> on Unprotect — simulates a
    /// tampered or expired token.
    /// Call this after <see cref="SetAuthenticatedUser"/> and before <see cref="CreateSut"/>.
    /// </summary>
    private void SetDataProtectorToThrow()
    {
        var protectorMock = new Mock<IDataProtector>();
        protectorMock
            .Setup(p => p.Unprotect(It.IsAny<byte[]>()))
            .Throws<CryptographicException>();

        _dataProtectionProviderMock = new Mock<IDataProtectionProvider>();
        _dataProtectionProviderMock
            .Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(protectorMock.Object);
    }

    /// <summary>
    /// Configures a mock <see cref="IDataProtectionProvider"/> whose protector
    /// returns the supplied <paramref name="plaintextPayload"/> from Unprotect —
    /// simulates a known token payload without real encryption.
    /// Call this after <see cref="SetAuthenticatedUser"/> and before <see cref="CreateSut"/>.
    /// </summary>
    private void SetDataProtectorToReturn(string plaintextPayload)
    {
        var protectorMock = new Mock<IDataProtector>();
        protectorMock
            .Setup(p => p.Unprotect(It.IsAny<byte[]>()))
            .Returns(System.Text.Encoding.UTF8.GetBytes(plaintextPayload));

        protectorMock
            .Setup(p => p.Protect(It.IsAny<byte[]>()))
            .Returns((byte[] input) => input);

        _dataProtectionProviderMock = new Mock<IDataProtectionProvider>();
        _dataProtectionProviderMock
            .Setup(p => p.CreateProtector(It.IsAny<string>()))
            .Returns(protectorMock.Object);
    }

    /// <summary>
    /// Protects the supplied <see cref="OtpVerificationSigning"/> using the real
    /// <see cref="IDataProtectionProvider"/> registered in the service collection.
    /// Use this to produce a raw token string that the real protector can Unprotect
    /// during a test — i.e. when no mock protector override is needed.
    /// </summary>
    private string ProtectSigning(OtpVerificationSigning signing)
    {
        using var scope = _serviceProvider.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>();
        var protector = provider.CreateProtector("Webhook.Otp.OtpVerificationSigning");
        string payload = JsonSerializer.Serialize(signing);
        byte[] protectedBytes = protector.Protect(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(protectedBytes);
    }

    private UserDeactivationRequestDto BuildUserDeactivateRequest(string usernameoremail = "test@mail.com", string justification = "This is for test purpose.") => new UserDeactivationRequestDto() { UserNameOrEmailAddress = usernameoremail, DeactivationJustification = justification };

    private ReactivateUserRequestDto BuildUserReactivationRequest(string usernameoremail = "test@mail.com") => new ReactivateUserRequestDto() { UserNameOrEmailAddress = usernameoremail };

    private UserService GetSut()
    {
        return new UserService(new RepositoryContext(_dbContextOptions), _userManagerMock.Object,dataProtectionProvider: _serviceProvider.GetRequiredService<IDataProtectionProvider>(), applicationHasher: _serviceProvider.GetRequiredService<IApplicationHasher>(), authenticatedUserDetails: _authenticatedUserDetailsMock.Object);
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

    // =============================================================================
    // DeactivateUserProfileAsync — unit tests
    // =============================================================================
    // Two distinct flows:
    //   Admin path   — role check bypasses all token validation
    //   Non-Admin path — must supply a valid, unconsumed OTP operation token
    //                    that matches the target user and passes hash validation
    // =============================================================================

    // =========================================================================
    // ADMIN PATH
    // =========================================================================

    [Fact]
    public async Task DeactivateUserProfileAsync_Admin_EmailProvided_Returns200OK()
    {
        // Arrange
        var seedResult = await SeedUserAsync();
        SetAuthenticatedUser(role: "Admin");

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert — response
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);

        // Assert — persisted state
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.NormalizedEmail!
                .Contains(request.UserNameOrEmailAddress.ToUpper()));

        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser!.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(
            request.DeactivationJustification,
            deactivatedUser.DeactivationJustification,
            ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_Admin_UsernameProvided_Returns200OK()
    {
        // Arrange
        var seedResult = await SeedUserAsync();
        SetAuthenticatedUser(role: "Admin");

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.userName);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert — response
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);

        // Assert — persisted state
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.NormalizedUserName!
                .Contains(request.UserNameOrEmailAddress.ToUpper()));

        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser!.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(
            request.DeactivationJustification,
            deactivatedUser.DeactivationJustification,
            ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_Admin_EmailNotExist_Returns404NotFound()
    {
        // Arrange
        SetAuthenticatedUser(role: "Admin");

        var request = BuildUserDeactivateRequest(usernameoremail: "nonexistent@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith(
            "User with details does not exist -",
            result.ResponseMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            request.UserNameOrEmailAddress,
            result.ResponseMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_Admin_UsernameNotExist_Returns404NotFound()
    {
        // Arrange
        SetAuthenticatedUser(role: "Admin");

        var request = BuildUserDeactivateRequest(usernameoremail: "nonexistentuser101");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith(
            "User with details does not exist -",
            result.ResponseMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            request.UserNameOrEmailAddress,
            result.ResponseMessage,
            StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // NON-ADMIN PATH — operation token validation
    // =========================================================================

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_NoOperationToken_Returns403Forbidden()
    {
        // Arrange — authenticated as USER with no operation token
        SetAuthenticatedUser(role: "USER", operationToken: "");

        var request = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Forbidden, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Credentials not provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_ExpiredOrTamperedToken_Returns400BadRequest()
    {
        // Arrange — data protector throws CryptographicException for a tampered token
        SetAuthenticatedUser(role: "USER", operationToken: "tampered-token");
        SetDataProtectorToThrow(); // configures _dataProtector.Unprotect to throw CryptographicException

        var request = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Token has expired. Kindly re-authenticate.", result.ResponseMessage, ignoreCase: true);
    }

    //[Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenDeserializesToNull_Returns400BadRequest()
    {
        // Arrange — protector succeeds but produces content that deserializes to null
        SetAuthenticatedUser(role: "USER", operationToken: "valid-protected-token");
        SetDataProtectorToReturn("null"); // unprotected payload deserializes to null

        var request = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Token could not be parsed successfully. Kindly re-authenticate.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenJtiNotAValidGuid_Returns400BadRequest()
    {
        // Arrange — token deserializes correctly but Jti is not a valid GUID
        var signing = BuildOtpVerificationSigning(jti: "not-a-guid");
        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenJtiNotFoundInDatabase_Returns400BadRequest()
    {
        // Arrange — token has a valid GUID JTI that does not exist in the database
        var signing = BuildOtpVerificationSigning(jti: Guid.NewGuid().ToString("N"));
        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: "user@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenAlreadyConsumed_Returns400BadRequest()
    {
        // Arrange — seed a token that is already consumed
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.DeactivateProfile,
            consumed: true);

        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenExpired_Returns400BadRequest()
    {
        // Arrange — seed a token whose ExpiresAt is in the past
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.DeactivateProfile,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenRevoked_Returns400BadRequest()
    {
        // Arrange — seed a token that has been revoked
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(seedResult.userId, purpose: OtpPurpose.DeactivateProfile, revoked: true);

        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenPurposeMismatch_Returns400BadRequest()
    {
        // Arrange — seed a token issued for a different purpose (e.g. PasswordReset)
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.PasswordReset); // wrong purpose

        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: ProtectSigning(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_HashValidationFails_Returns400BadRequest()
    {
        // Arrange — seed a valid token but present a different raw token string
        // so the hash comparison fails
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.DeactivateProfile);

        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        // The raw token passed does not match what was hashed at seed time
        SetAuthenticatedUser(role: "USER", operationToken: Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        SetDataProtectorToReturn(JsonSerializer.Serialize(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_UserNotFound_Returns404NotFound()
    {
        // Arrange — valid token but the user it references does not exist in the database
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId, purpose: OtpPurpose.DeactivateProfile);

        var rawToken = tokenSeedResult.rawToken;
        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: rawToken);
        SetDataProtectorToReturn(JsonSerializer.Serialize(signing));

        // Use an identifier that does not exist
        var request = BuildUserDeactivateRequest(usernameoremail: "ghost@example.com");
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.StartsWith("User with details does not exist -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_TokenIssuedForDifferentUser_Returns400BadRequest()
    {
        // Arrange — token is issued for userA but the request targets userB
        var userA = await SeedUserAsync();
        var userB = await SeedUserAsync(emailAddress: "test2@mail.com", username: "testeduser112");
        var tokenSeedResult = await SeedOperationTokenAsync(
            userA.userId,
            purpose: OtpPurpose.DeactivateProfile);

        var rawToken = tokenSeedResult.rawToken;
        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: userA.normalizedEmail); // token issued for userA

        SetAuthenticatedUser(role: "USER", operationToken: rawToken);
        SetDataProtectorToReturn(JsonSerializer.Serialize(signing));

        // Request targets userB
        var request = BuildUserDeactivateRequest(usernameoremail: userB.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("Operation Failed.", result.ResponseData, ignoreCase: true);
        Assert.Equal("Invalid Token provided.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_ValidToken_EmailProvided_Returns200OK()
    {
        // Arrange
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.DeactivateProfile);

        var rawToken = tokenSeedResult.rawToken;
        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: rawToken);
        SetDataProtectorToReturn(JsonSerializer.Serialize(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.email);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert — response
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);

        // Assert — persisted user state
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.NormalizedEmail!
                .Contains(request.UserNameOrEmailAddress.ToUpper()));

        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser!.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(
            request.DeactivationJustification,
            deactivatedUser.DeactivationJustification,
            ignoreCase: true);

        // Assert — token consumed
        using var tokenScope = _serviceProvider.CreateScope();
        var dbContext = tokenScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var consumedToken = await dbContext.OtpOperationTokens
            .Include(x => x.OtpVerification)
            .FirstOrDefaultAsync(x => x.Jti == tokenSeedResult.jti);

        Assert.NotNull(consumedToken);
        Assert.NotNull(consumedToken!.ConsumedAt);
        Assert.NotNull(consumedToken.OtpVerification.ConsumedAt);
        Assert.True(consumedToken.OtpVerification.IsConsumed);
    }

    [Fact]
    public async Task DeactivateUserProfileAsync_NonAdmin_ValidToken_UsernameProvided_Returns200OK()
    {
        // Arrange
        var seedResult = await SeedUserAsync();
        var tokenSeedResult = await SeedOperationTokenAsync(
            seedResult.userId,
            purpose: OtpPurpose.DeactivateProfile);

        var rawToken = tokenSeedResult.rawToken;
        var signing = BuildOtpVerificationSigning(
            jti: tokenSeedResult.jti.ToString(),
            issuedFor: seedResult.normalizedEmail);

        SetAuthenticatedUser(role: "USER", operationToken: rawToken);
        SetDataProtectorToReturn(JsonSerializer.Serialize(signing));

        var request = BuildUserDeactivateRequest(usernameoremail: seedResult.userName);
        var sut = CreateSut();

        // Act
        var result = await sut.DeactivateUserProfileAsync(request);

        // Assert — response
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("User profile successfully deactivated.", result.ResponseMessage, ignoreCase: true);

        // Assert — persisted user state
        using var scope = _serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var deactivatedUser = await userManager.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.NormalizedUserName!
                .Contains(request.UserNameOrEmailAddress.ToUpper()));

        Assert.NotNull(deactivatedUser);
        Assert.False(deactivatedUser!.IsActive);
        Assert.NotNull(deactivatedUser.DeletedAt);
        Assert.NotNull(deactivatedUser.DeactivationJustification);
        Assert.Equal(
            request.DeactivationJustification,
            deactivatedUser.DeactivationJustification,
            ignoreCase: true);

        // Assert — token consumed
        using var tokenScope = _serviceProvider.CreateScope();
        var dbContext = tokenScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var consumedToken = await dbContext.OtpOperationTokens
            .Include(x => x.OtpVerification)
            .FirstOrDefaultAsync(x => x.Jti == tokenSeedResult.jti);

        Assert.NotNull(consumedToken);
        Assert.NotNull(consumedToken!.ConsumedAt);
        Assert.NotNull(consumedToken.OtpVerification.ConsumedAt);
        Assert.True(consumedToken.OtpVerification.IsConsumed);
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

        // Deactivate first using Admin context via CreateSut so token validation is bypassed
        SetAuthenticatedUser(role: "Admin");
        var deactivateResult = await CreateSut().DeactivateUserProfileAsync(deactivateRequest);
        Assert.True(deactivateResult.IsSuccessful, $"Deactivate step failed: {deactivateResult.ResponseMessage}");

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

        // Deactivate first using Admin context via CreateSut so token validation is bypassed
        SetAuthenticatedUser(role: "Admin");
        var deactivateResult = await CreateSut().DeactivateUserProfileAsync(deactivateRequest);
        Assert.True(deactivateResult.IsSuccessful, $"Deactivate step failed: {deactivateResult.ResponseMessage}");



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