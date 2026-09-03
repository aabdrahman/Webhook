using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Security.Cryptography;
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

public class OtpServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private readonly string _tempDirectory;
    private readonly string _templateDirectory;
    private readonly Mock<IApplicationHasher> _applicationHasherMock;
    private ServiceProvider _serviceProvider;
    public OtpServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;

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

        _applicationHasherMock = new Mock<IApplicationHasher>();
    }

    private const string DefaultUserRole = "USER";
    private const string DefaultPassword = "Test@1234!";

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.Configure<TokenValidationConfiguration>(opts =>
        {
            opts.OtpExpirationAfterInSeconds = 60;
            opts.OtpOperationTokenExpiresAFterInSceonds = 90;
        });

        services.Configure<OtpSettingsConfiguration>(opts =>
        {
            opts.OtpToGenerateLength = 6;
            opts.MaximumOtpLength = 12;
        });

        var environment = new TestWebHostEnvironment
        {
            EnvironmentName = Environments.Development,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            ContentRootFileProvider = new PhysicalFileProvider(AppContext.BaseDirectory)
        };

        services.AddMemoryCache();

        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddScoped<IAuthenticatedUserDetails, AuthenticatedUserDetails>();
        services.AddScoped<ICacheService, CacheService>();


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

    private async Task<(string email, string userName)> SeedUserAsync(string emailAddress = "test@mail.com", string username = "testUser112", string? password = DefaultPassword)
    {
        using var scope = _serviceProvider.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var dto = BuildUserToCreate(emailAddress: emailAddress, username: username, password: password);
        var result = await userService.CreateUserAsync(dto);

        Assert.True(result.IsSuccessful, $"Seed user failed: {result.ResponseMessage}");
        return (dto.EmailAddress, dto.UserName);
    }

    private RequestOtpDto BuildOtpRequest(string usernameoremail = "test@mail.com", OtpPurpose purpose = OtpPurpose.PasswordReset) => new RequestOtpDto() { Purpose = purpose, UserNameOrEmailAddress = usernameoremail };

    private CreateUserDto BuildUserToCreate(string emailAddress = "test@mail.com", string password = DefaultPassword, string firstName = "John", string lastName = "Doe", string username = "testUser112") => new CreateUserDto()
    {
        EmailAddress = emailAddress,
        Password = password,
        ConfirmPassword = password,
        FirstName = firstName,
        LastName = lastName,
        UserName = username
    };

    private OtpVerificationRequestDto BuildOtpVerificationRequest(string emailorusername = "", string otp = "") => new OtpVerificationRequestDto()
    {
        EmailAddress = emailorusername,
        Otp = otp
    };

    private OtpVerification BuildOtpVerification(Guid userId, bool isConsumed = false) => new OtpVerification()
    {
        UserId = userId,
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(120),
        IsConsumed = isConsumed,
        Purpose = OtpPurpose.PasswordReset,
        OtpHash = RandomNumberGenerator.GetHexString(12)
    };

    private OtpService CreateSut(IApplicationHasher applicationHasher = null) => new OtpService(
                                            _serviceProvider.GetRequiredService<RepositoryContext>(),
                                            _serviceProvider.GetRequiredService<UserManager<User>>(),
                                            applicationHasher ?? _serviceProvider.GetRequiredService<IApplicationHasher>(),
                                            _serviceProvider.GetRequiredService<IOptionsMonitor<TokenValidationConfiguration>>(),
                                            _serviceProvider.GetRequiredService<IDataProtectionProvider>());


    [Fact]
    public async Task RevokeUserOtpAsync_NoUser_Returns202NoContent()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.RevokeUserOtpAsync(Guid.NewGuid(), CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NoContent, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal("No unconsumed otps revoked for user.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task RevokeUserOtpAsync_UserAndOtpExists_Returns200OK()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        Guid seedUserId = Guid.NewGuid(); int totalCreated = 0;
        using (var requestScope = _serviceProvider.CreateScope())
        {
            var authService = requestScope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var usermanager = requestScope.ServiceProvider.GetRequiredService<UserManager<User>>();

            for (int i = 0; i < 3; i++)
            {
                var seedResult = await authService.RequestOtpAsync(BuildOtpRequest(), CancellationToken.None);
                Assert.True(seedResult.IsSuccessful, seedResult.ResponseMessage);
                totalCreated++;
            }

            var seededUser = await usermanager.FindByEmailAsync(seedUserResult.email);
            Assert.NotNull(seededUser);
            seedUserId = seededUser.Id;
        }
        var sut = CreateSut();

        //Act
        var result = await sut.RevokeUserOtpAsync(seedUserId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal($"{totalCreated} OTPs revoked successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var otps = await assertCtx.OtpVerifications.ToListAsync();

        foreach (var otp in otps)
        {
            Assert.True(otp.RevokedAt.HasValue, $"OTP with id: {otp.Id} is yet to be revoked.");
        }
    }

    [Fact]
    public async Task RevokeUserOtpAsync_ExpiredOtps_NotPicked()
    {
        //Arrange
        var seedUserResult = await SeedUserAsync();
        Guid seedUserId = Guid.NewGuid();
        using (var requestScope = _serviceProvider.CreateScope())
        {
            var requstCtx = requestScope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var usermanager = requestScope.ServiceProvider.GetRequiredService<UserManager<User>>();


            var seededUser = await usermanager.FindByEmailAsync(seedUserResult.email);
            Assert.NotNull(seededUser);
            seedUserId = seededUser.Id;

            var expiredOtp = BuildOtpVerification(seedUserId);
            expiredOtp.CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-60);
            expiredOtp.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-30);

            var unconsumedOtp1 = BuildOtpVerification(seedUserId);
            var unconsumedOtp2 = BuildOtpVerification(seedUserId);

            await requstCtx.OtpVerifications.AddRangeAsync(expiredOtp, unconsumedOtp1, unconsumedOtp2);
            await requstCtx.SaveChangesAsync();
        }
        var sut = CreateSut();

        //Act
        var result = await sut.RevokeUserOtpAsync(seedUserId);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal($"2 OTPs revoked successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var otps = await assertCtx.OtpVerifications.ToListAsync();

        Assert.Single(otps.Where(x => !x.RevokedAt.HasValue).ToList());
        Assert.Equal(2, otps.Where(x => x.RevokedAt.HasValue).Count());
    }

    [Fact]
    public async Task RevokeUserOtpAsync_PickOnlyProvidedUserId()
    {
        //Arrange
        var seedUserResult1 = await SeedUserAsync();
        var seedUserResult2 = await SeedUserAsync(emailAddress: "userexample@mail.com", username: "exmapleuser001", password: "PasswordTest#111");
        Guid seedUserId1 = Guid.NewGuid();
        Guid seedUserId2 = Guid.NewGuid();
        using (var requestScope = _serviceProvider.CreateScope())
        {
            var requstCtx = requestScope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var usermanager = requestScope.ServiceProvider.GetRequiredService<UserManager<User>>();


            var seededUser1 = await usermanager.FindByEmailAsync(seedUserResult1.email);
            var seededUser2 = await usermanager.FindByEmailAsync(seedUserResult2.email);
            Assert.NotNull(seededUser1);
            Assert.NotNull(seededUser2);
            seedUserId1 = seededUser1.Id;
            seedUserId2 = seededUser2.Id;

            var unconsumedOtp = BuildOtpVerification(seedUserId1);

            var unconsumedOtp1 = BuildOtpVerification(seedUserId2);
            var unconsumedOtp2 = BuildOtpVerification(seedUserId2);

            await requstCtx.OtpVerifications.AddRangeAsync(unconsumedOtp, unconsumedOtp1, unconsumedOtp2);
            await requstCtx.SaveChangesAsync();
        }
        var sut = CreateSut();
        //Act
        var result = await sut.RevokeUserOtpAsync(seedUserId1);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("Operation Successful.", result.ResponseData, ignoreCase: true);
        Assert.Equal($"1 OTPs revoked successfully.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var otps = await assertCtx.OtpVerifications.ToListAsync();

        Assert.Single(otps.Where(x => x.RevokedAt.HasValue).ToList());
        Assert.Equal(2, otps.Where(x => !x.RevokedAt.HasValue).Count());
    }

    [Fact]
    public async Task ValidateOtpAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var seedUserResut = await SeedUserAsync();
        string generatedOtp = string.Empty; Guid userId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var requestCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var requestOtpGenerator = scope.ServiceProvider.GetRequiredService<IOtpGenerator>();
            var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var requestAppHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

            var seededUser = await usermanager.FindByEmailAsync(seedUserResut.email);
            Assert.NotNull(seededUser);
            userId = seededUser.Id;

            generatedOtp = requestOtpGenerator.GenerateOtp();
            string hashedOtp = await requestAppHasher.HashSecret(generatedOtp);

            OtpVerification otpVerificationRceord = BuildOtpVerification(userId: userId);
            otpVerificationRceord.OtpHash = hashedOtp;
            await requestCtx.AddAsync(otpVerificationRceord);
            await requestCtx.SaveChangesAsync();

        }
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(emailorusername: seedUserResut.email, otp: generatedOtp), CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("OTP Veriifcation Successful. Token issued for operation.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var assertAppHasher = assertScope.ServiceProvider.GetRequiredService<IApplicationHasher>();

        List<OtpVerification> updatedOtpVerificationRecord = await assertCtx.OtpVerifications.Include(x => x.OperationTokens).Where(x => x.UserId == userId).ToListAsync();
        Assert.NotNull(updatedOtpVerificationRecord);
        Assert.Single(updatedOtpVerificationRecord);
        Assert.Single(updatedOtpVerificationRecord.First().OperationTokens);
        Assert.NotNull(updatedOtpVerificationRecord.First().ValidatedAt);
        Assert.Null(updatedOtpVerificationRecord.First().ConsumedAt);
        Assert.Null(updatedOtpVerificationRecord.First().RevokedAt);
        Assert.False(updatedOtpVerificationRecord.First().IsConsumed);

        Assert.True(await assertAppHasher.ValidateHashedSecret(updatedOtpVerificationRecord.First().OperationTokens.First().TokenHash, result.ResponseData.SignedToken));

    }

    [Fact]
    public async Task ValidateOtpAsync_InvalidUser_Returns400BadRequest()
    {
        //Arrange
        var request = BuildOtpVerificationRequest(emailorusername: "test@mail.com", otp: "123445");
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("OTP Verification Failed. Invalid Credentials.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ValidateOtpAsync_ValidUserNoOtpRecord_Returns400BadRequest()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        var sut = CreateSut();
        var request = BuildOtpVerificationRequest(emailorusername: seedResult.email, otp: "123456");

        //Act
        var result = await sut.ValidateOtpAsync(request);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("OTP Verification Failed. OTP Expired.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ValidateOtpAsync_InvalidOtp_ReturnsBadRequest()
    {
        //Arrange
        var seedUserResut = await SeedUserAsync();
        string generatedOtp = string.Empty; Guid userId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var requestCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var requestOtpGenerator = scope.ServiceProvider.GetRequiredService<IOtpGenerator>();
            var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var requestAppHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

            var seededUser = await usermanager.FindByEmailAsync(seedUserResut.email);
            Assert.NotNull(seededUser);
            userId = seededUser.Id;

            generatedOtp = requestOtpGenerator.GenerateOtp();
            string hashedOtp = await requestAppHasher.HashSecret(generatedOtp);

            OtpVerification otpVerificationRceord = BuildOtpVerification(userId: userId);
            otpVerificationRceord.OtpHash = hashedOtp;
            await requestCtx.AddAsync(otpVerificationRceord);
            await requestCtx.SaveChangesAsync();

        }
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(otp: $"{generatedOtp}3", emailorusername: seedUserResut.email));

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("OTP Verification Failed. Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        List<OtpVerification> updatedOtpVerificationRecord = await assertCtx.OtpVerifications.Include(x => x.OperationTokens).Where(x => x.UserId == userId).ToListAsync();
        Assert.NotNull(updatedOtpVerificationRecord);
        Assert.Single(updatedOtpVerificationRecord);
        Assert.Empty(updatedOtpVerificationRecord.First().OperationTokens);
        Assert.Null(updatedOtpVerificationRecord.First().ValidatedAt);
        Assert.Null(updatedOtpVerificationRecord.First().ConsumedAt);
        Assert.Null(updatedOtpVerificationRecord.First().RevokedAt);
        Assert.False(updatedOtpVerificationRecord.First().IsConsumed);
    }

    [Fact]
    public async Task ValidateOtpAsync_ValidOtpSecretHashFailed_ReturnsBadRequest()
    {
        //Arrange
        var seedUserResut = await SeedUserAsync();
        string generatedOtp = string.Empty; Guid userId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var requestCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var requestOtpGenerator = scope.ServiceProvider.GetRequiredService<IOtpGenerator>();
            var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var requestAppHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

            var seededUser = await usermanager.FindByEmailAsync(seedUserResut.email);
            Assert.NotNull(seededUser);
            userId = seededUser.Id;

            generatedOtp = requestOtpGenerator.GenerateOtp();
            string hashedOtp = await requestAppHasher.HashSecret(generatedOtp);

            OtpVerification otpVerificationRceord = BuildOtpVerification(userId: userId);
            otpVerificationRceord.OtpHash = hashedOtp;
            await requestCtx.AddAsync(otpVerificationRceord);
            await requestCtx.SaveChangesAsync();

        }
        _applicationHasherMock.Setup(x => x.HashSecret(It.IsAny<string>())).ReturnsAsync("");
        var sut = CreateSut(applicationHasher: _applicationHasherMock.Object);

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(otp: generatedOtp, emailorusername: seedUserResut.email));

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Equal("OTP Verification Failed. Invalid Credentials.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        List<OtpVerification> updatedOtpVerificationRecord = await assertCtx.OtpVerifications.Include(x => x.OperationTokens).Where(x => x.UserId == userId).ToListAsync();
        Assert.NotNull(updatedOtpVerificationRecord);
        Assert.Single(updatedOtpVerificationRecord);
        Assert.Empty(updatedOtpVerificationRecord.First().OperationTokens);
        Assert.Null(updatedOtpVerificationRecord.First().ValidatedAt);
        Assert.Null(updatedOtpVerificationRecord.First().ConsumedAt);
        Assert.Null(updatedOtpVerificationRecord.First().RevokedAt);
        Assert.False(updatedOtpVerificationRecord.First().IsConsumed);
    }

    [Fact]
    public async Task ValidateOtpAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        var seedResult = await SeedUserAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(emailorusername: seedResult.email, "123456"), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("An error occurred while validating your OTP. Kindly retry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ValidateOtpAsync_OldOtp_ReturnsBadRequest()
    {
        //Arrange
        var seedUserResut = await SeedUserAsync();
        string generatedOtp = string.Empty; Guid userId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var requestCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var requestOtpGenerator = scope.ServiceProvider.GetRequiredService<IOtpGenerator>();
            var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var requestAppHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

            var seededUser = await usermanager.FindByEmailAsync(seedUserResut.email);
            Assert.NotNull(seededUser);
            userId = seededUser.Id;

            generatedOtp = requestOtpGenerator.GenerateOtp();
            string hashedOtp = await requestAppHasher.HashSecret(generatedOtp);

            OtpVerification otpVerificationRecord = BuildOtpVerification(userId: userId);
            otpVerificationRecord.OtpHash = hashedOtp;
            otpVerificationRecord.CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10);

            string generatedOtp2 = requestOtpGenerator.GenerateOtp();
            string hashedOtp2 = await requestAppHasher.HashSecret(generatedOtp2);
            OtpVerification otpVerificationRecord2 = BuildOtpVerification(userId: userId);
            otpVerificationRecord.OtpHash = hashedOtp2;

            await requestCtx.AddRangeAsync(otpVerificationRecord, otpVerificationRecord2);
            await requestCtx.SaveChangesAsync();

        }
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(emailorusername: seedUserResut.email, otp: generatedOtp));

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
        Assert.Equal("OTP Verification Failed. Invalid Credentials.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task ValidateOtpAsync_MostRecentOtp_Returns200OK()
    {
        //Arrange
        var seedUserResut = await SeedUserAsync();
        string generatedOtp = string.Empty; Guid userId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();

            var requestCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var requestOtpGenerator = scope.ServiceProvider.GetRequiredService<IOtpGenerator>();
            var usermanager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var requestAppHasher = scope.ServiceProvider.GetRequiredService<IApplicationHasher>();

            var seededUser = await usermanager.FindByEmailAsync(seedUserResut.email);
            Assert.NotNull(seededUser);
            userId = seededUser.Id;

            generatedOtp = requestOtpGenerator.GenerateOtp();
            string hashedOtp = await requestAppHasher.HashSecret(generatedOtp);

            OtpVerification otpVerificationRecord = BuildOtpVerification(userId: userId);
            otpVerificationRecord.OtpHash = hashedOtp;
            //otpVerificationRecord.CreatedAt = DateTimeOffset.UtcNow;


            string generatedOtp2 = requestOtpGenerator.GenerateOtp();
            string hashedOtp2 = await requestAppHasher.HashSecret(generatedOtp2);

            OtpVerification otpVerificationRecord2 = BuildOtpVerification(userId: userId);
            otpVerificationRecord2.OtpHash = hashedOtp2;
            otpVerificationRecord2.CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10);

            await requestCtx.AddRangeAsync(otpVerificationRecord, otpVerificationRecord2);
            await requestCtx.SaveChangesAsync();

        }
        var sut = CreateSut();

        //Act
        var result = await sut.ValidateOtpAsync(BuildOtpVerificationRequest(emailorusername: seedUserResut.email, otp: generatedOtp));

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Equal("OTP Veriifcation Successful. Token issued for operation.", result.ResponseMessage, ignoreCase: true);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var assertAppHasher = assertScope.ServiceProvider.GetRequiredService<IApplicationHasher>();

        List<OtpVerification> updatedOtpVerificationRecord = await assertCtx.OtpVerifications.OrderByDescending(x => x.CreatedAt).Include(x => x.OperationTokens).Where(x => x.UserId == userId).ToListAsync();
        Assert.NotNull(updatedOtpVerificationRecord);
        Assert.Equal(2, updatedOtpVerificationRecord.Count);
        Assert.Single(updatedOtpVerificationRecord.First().OperationTokens);
        Assert.NotNull(updatedOtpVerificationRecord.First().ValidatedAt);
        Assert.Null(updatedOtpVerificationRecord.First().ConsumedAt);
        Assert.Null(updatedOtpVerificationRecord.First().RevokedAt);
        Assert.False(updatedOtpVerificationRecord.First().IsConsumed);

        Assert.True(await assertAppHasher.ValidateHashedSecret(updatedOtpVerificationRecord.First().OperationTokens.First().TokenHash, result.ResponseData.SignedToken));

        Assert.Empty(updatedOtpVerificationRecord.OrderBy(x => x.CreatedAt).First().OperationTokens);
        Assert.Null(updatedOtpVerificationRecord.OrderBy(x => x.CreatedAt).First().ValidatedAt);
        Assert.Null(updatedOtpVerificationRecord.OrderBy(x => x.CreatedAt).First().ConsumedAt);
        Assert.Null(updatedOtpVerificationRecord.OrderBy(x => x.CreatedAt).First().RevokedAt);
        Assert.False(updatedOtpVerificationRecord.OrderBy(x => x.CreatedAt).First().IsConsumed);

    }
}
