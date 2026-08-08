using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public class UserServiceTests
{
    private readonly Mock<UserManager<User>> _userManagerMock;
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;

    public UserServiceTests()
    {
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
                                        .UseInMemoryDatabase(Guid.NewGuid().ToString())
                                        .Options;

        _userManagerMock = CreateUserManagerMock();
    }

    private UserService GetSut()
    {
        return new UserService(new RepositoryContext(_dbContextOptions), _userManagerMock.Object);
    }

    private  Mock<UserManager<User>> CreateUserManagerMock()
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
}
