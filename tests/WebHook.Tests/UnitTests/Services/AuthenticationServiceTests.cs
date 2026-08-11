using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public class AuthenticationServiceTests
{
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly Mock<SignInManager<User>> _signinManagerMock;
    private readonly Mock<IOptionsMonitor<JwtSettingsConfiguration>> _settingsConfigMock;

    public AuthenticationServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                        .Options;

        _userManagerMock = CreateUserManagerMock();
        _signinManagerMock = CreateSignInManagerMock(_userManagerMock);
        _settingsConfigMock = new Mock<IOptionsMonitor<JwtSettingsConfiguration>>();

    }

    private Infrastructure.Services.AuthenticationService GetSut()
    {
        return new Infrastructure.Services.AuthenticationService(_userManagerMock.Object, new RepositoryContext(_dbContextOptions), _settingsConfigMock.Object, _signinManagerMock.Object);
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
    public async Task LoginUserAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        var sut = GetSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.LoginUserAsync(new Core.DataTransferObjects.Authentication.LoginUserDto() { Password = "TestPassword", UserNameOrEmailAddress = "test@mail.com" }, cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ErrorDetail);
        Assert.Null(result.ResponseData);
        Assert.NotNull(result.ResponseMessage);
        Assert.Equal("An error occurred.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task LoginUserAsync_ValidRequest_Returns200()
    {
        //Arrange
        var sut = GetSut();
        var request = new LoginUserDto()
        {
            Password = "TestPassword!!!!",
            UserNameOrEmailAddress = "test@mail.com"
        };

        _settingsConfigMock.Setup(x => x.CurrentValue)
            .Returns(new JwtSettingsConfiguration()
            {
                ValidIssuer = "Test Issuer",
                ValidAudiences = "TestAudience1",
                TokenExpirationAfterInSeconds = 30,
                RefreshTokenExpirationAfterInSeconds = 60
            });

        var userToAuthenticate = new User() { NormalizedEmail = request.UserNameOrEmailAddress, IsActive = true };

        _signinManagerMock.Setup(x => x.CheckPasswordSignInAsync(It.IsAny<User>(), request.Password, true))
            .ReturnsAsync(SignInResult.Success);

        _userManagerMock.Setup(x => x.FindByEmailAsync(request.UserNameOrEmailAddress))
            .ReturnsAsync(userToAuthenticate);

        _userManagerMock.Setup(x => x.CheckPasswordAsync(It.IsAny<User>(), request.Password))
            .ReturnsAsync(true);

        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<User>()))
            .ReturnsAsync(IdentityResult.Success);

        //Act
        var result = await sut.LoginUserAsync(request, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("User signed in successfully.", result.ResponseMessage, ignoreCase: true);
    }
}
