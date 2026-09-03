using Moq;
using System.Net;
using System.Net.Http.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Interfaces.Services;
using WebHook.IntegrationTests.Controllers;

namespace WebHook.IntegrationTests.Controllers.Users;

/// <summary>
/// HTTP-level integration tests for <see cref="UsersController"/>.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>POST api/Users/register    — Register    [AllowAnonymous]</description></item>
///   <item><description>POST api/Users/deactivate  — Deactivate  [Authorize]</description></item>
///   <item><description>POST api/Users/reactivate  — Reactivate  [Authorize(Roles="Admin")]</description></item>
/// </list>
/// </summary>
public sealed class UsersControllerIntegrationTests
    : IClassFixture<WebApiFactory>, IAsyncLifetime
{
    private readonly WebApiFactory _factory;
    private HttpClient _client = null!;

    public UsersControllerIntegrationTests(WebApiFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        // Bearer header for protected endpoints (Deactivate, Reactivate)
        _client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CreateUserDto BuildCreateUserDto(
        string? email    = null,
        string? userName = null) => new()
        {
            FirstName       = "John",
            LastName        = "Doe",
            EmailAddress    = email    ?? "user@test.com",
            UserName        = userName ?? "johndoe",
            Password        = "Testedok@1234!",
            ConfirmPassword = "Testedok@1234!"
        };

    private static UserDeactivationRequestDto BuildDeactivationDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress    = userNameOrEmail,
            DeactivationJustification = "Test deactivation."
        };

    private static ReactivateUserRequestDto BuildReactivationDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail
        };

    // =========================================================================
    // POST api/Users/register — [AllowAnonymous]
    // =========================================================================

    [Fact]
    public async Task Register_NoAuthToken_StillReachesController()
    {
        // Arrange — public endpoint
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Conflict.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert — public so must not return 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Register_ValidRequest_Returns201Created()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "User created successfully.", HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409Conflict()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User with email already exists.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Returns409Conflict()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Username already taken.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Your profile could not be created.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_UsernmaeContainsInvalidCharacter_Returns400()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Your profile could not be created.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto(userName: "tested@001"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ForwardsRequestBodyToService()
    {
        // Arrange
        CreateUserDto? captured = null;
        var request = BuildCreateUserDto("specific@test.com", "specificuser");

        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateUserDto, CancellationToken>((dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/Users/register", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("specific@test.com", captured!.EmailAddress);
        Assert.Equal("specificuser",      captured.UserName);
    }

    [Fact]
    public async Task Register_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Register_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        _factory.UserServiceMock.Verify(
            s => s.CreateUserAsync(
                It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // POST api/Users/deactivate — [Authorize]
    // =========================================================================

    [Fact]
    public async Task Deactivate_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/Users/deactivate", BuildDeactivationDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_ValidRequest_Returns200()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "User deactivated successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/deactivate", BuildDeactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_UserNotFound_Returns404()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/deactivate", BuildDeactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_Returns400()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Account is already inactive.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/deactivate", BuildDeactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/deactivate", BuildDeactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Deactivated.", HttpStatusCode.OK));

        // Act
        await _client.PostAsJsonAsync("/api/Users/deactivate", BuildDeactivationDto());

        // Assert
        _factory.UserServiceMock.Verify(
            s => s.DeactivateUserProfileAsync(
                It.IsAny<UserDeactivationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // POST api/Users/reactivate — [Authorize(Roles = "Admin")]
    // =========================================================================

    [Fact]
    public async Task Reactivate_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/Users/reactivate", BuildReactivationDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_ValidRequest_Returns200()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "User profile successfully reactivated.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/reactivate", BuildReactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.Contains("reactivated", body.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reactivate_UserNotFound_Returns404()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/reactivate", BuildReactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_AlreadyActive_Returns400()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Account is already active.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/reactivate", BuildReactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Users/reactivate", BuildReactivationDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Reactivate_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Reactivated.", HttpStatusCode.OK));

        // Act
        await _client.PostAsJsonAsync("/api/Users/reactivate", BuildReactivationDto());

        // Assert
        _factory.UserServiceMock.Verify(
            s => s.ReactivateUserProfileAsync(
                It.IsAny<ReactivateUserRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
