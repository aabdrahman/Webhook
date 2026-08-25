using Moq;
using System.Net;
using System.Net.Http.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.Interfaces.Services;
using WebHook.IntegrationTests.Controllers;

namespace WebHook.IntegrationTests.Controllers.Authentication;

/// <summary>
/// HTTP-level integration tests for <see cref="AuthenticationController"/>.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>POST api/Authentication/login          — LoginUser      [AllowAnonymous]</description></item>
///   <item><description>POST api/Authentication/change-password — ChangePassword [Authorize via global filter]</description></item>
///   <item><description>POST api/Authentication/request-otp    — RequestOTP     [AllowAnonymous]</description></item>
///   <item><description>POST api/Authentication/refresh         — RefreshSession [AllowAnonymous]</description></item>
/// </list>
/// </summary>
public sealed class AuthenticationControllerIntegrationTests
    : IClassFixture<WebApiFactory>, IAsyncLifetime
{
    private readonly WebApiFactory _factory;
    private HttpClient _client = null!;

    public AuthenticationControllerIntegrationTests(WebApiFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        // Set Bearer header for endpoints that go through CustomAuthenticationFilter
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

    private static LoginUserDto BuildLoginDto(
        string userNameOrEmail = "user@test.com",
        string password        = "Test@1234!") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail,
            Password               = password
        };

    private static ChangePasswordDto BuildChangePasswordDto() => new()
    {
        UserNameOrEmailAddress = "user@test.com",
        OldPassword            = "OldPass@1234!",
        NewPassword            = "NewPass@1234!",
        ConfirmNewPassword     = "NewPass@1234!"
    };

    private static RequestOtpDto BuildRequestOtpDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail,
            Purpose                = 0
        };

    private static TokenDto BuildTokenDto(
        string access  = "access-token",
        string refresh = "refresh-token") => new(access, refresh);

    // =========================================================================
    // POST api/Authentication/login — [AllowAnonymous]
    // =========================================================================

    [Fact]
    public async Task Login_NoAuthToken_StillReachesController()
    {
        // Arrange — public endpoint
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/login", BuildLoginDto());

        // Assert — public so must not return 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var tokenDto = BuildTokenDto("access-token-value", "refresh-token-value");

        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Success(
                tokenDto, "User signed in successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("access-token-value",  body.ResponseData!.accessToken);
        Assert.Equal("refresh-token-value", body.ResponseData.refreshToken);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns404()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid Credentials.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task Login_AccountLockedOut_Returns400()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "User profile locked out.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ForwardsCredentialsToService()
    {
        // Arrange
        LoginUserDto? captured = null;

        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<LoginUserDto, CancellationToken>((dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Not found.", HttpStatusCode.NotFound));

        var request = BuildLoginDto("specific@test.com", "SpecificPass@1!");

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/login", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("specific@test.com", captured!.UserNameOrEmailAddress);
        Assert.Equal("SpecificPass@1!",   captured.Password);
    }

    [Fact]
    public async Task Login_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Login_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        _factory.AuthenticationServiceMock.Verify(
            s => s.LoginUserAsync(
                It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // POST api/Authentication/change-password — requires auth via global filter
    // =========================================================================

    [Fact]
    public async Task ChangePassword_NoAuthToken_Returns401()
    {
        // ChangePassword has no [AllowAnonymous] — CustomAuthenticationFilter rejects
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ValidRequest_Returns200()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Password changed successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_Returns400()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Current password is incorrect.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_UserNotFound_Returns404()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Changed.", HttpStatusCode.OK));

        // Act
        await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        _factory.AuthenticationServiceMock.Verify(
            s => s.ChangePasswordAsync(
                It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // POST api/Authentication/request-otp — [AllowAnonymous]
    // =========================================================================

    [Fact]
    public async Task RequestOtp_NoAuthToken_StillReachesController()
    {
        // Arrange — public endpoint
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.AuthenticationServiceMock
            .Setup(s => s.RequestOtpAsync(
                It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/request-otp", BuildRequestOtpDto());

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestOtp_ValidRequest_Returns200()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RequestOtpAsync(
                It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "OTP sent successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/request-otp", BuildRequestOtpDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RequestOtp_UserNotFound_Returns404()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RequestOtpAsync(
                It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/request-otp", BuildRequestOtpDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestOtp_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RequestOtpAsync(
                It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/request-otp", BuildRequestOtpDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // POST api/Authentication/refresh — [AllowAnonymous] — NEW endpoint
    // =========================================================================

    [Fact]
    public async Task RefreshSession_NoAuthToken_StillReachesController()
    {
        // Arrange — public endpoint
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid token.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/refresh", BuildTokenDto());

        // Assert
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RefreshSession_ValidToken_Returns200WithNewTokens()
    {
        // Arrange
        var newTokens = BuildTokenDto("new-access-token", "new-refresh-token");

        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Success(
                newTokens, "Session refreshed successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/refresh", BuildTokenDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("new-access-token",  body.ResponseData!.accessToken);
        Assert.Equal("new-refresh-token", body.ResponseData.refreshToken);
    }

    [Fact]
    public async Task RefreshSession_ExpiredOrInvalidToken_Returns400()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid Credentials.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/refresh", BuildTokenDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task RefreshSession_ForwardsTokenDtoToService()
    {
        // Arrange
        TokenDto? captured = null;
        var request = BuildTokenDto("sent-access", "sent-refresh");

        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .Callback<TokenDto, CancellationToken>((dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid.", HttpStatusCode.BadRequest));

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/refresh", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("sent-access",  captured!.accessToken);
        Assert.Equal("sent-refresh", captured.refreshToken);
    }

    [Fact]
    public async Task RefreshSession_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/refresh", BuildTokenDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task RefreshSession_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid.", HttpStatusCode.BadRequest));

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/refresh", BuildTokenDto());

        // Assert
        _factory.AuthenticationServiceMock.Verify(
            s => s.RefreshTokenAsync(
                It.IsAny<TokenDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
