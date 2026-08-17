using Moq;
using Serilog;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.Authentication;
using WebHook.Core.DataTransferObjects.OtpOperation;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="AuthenticationController"/>,
/// <see cref="UsersController"/>, and <see cref="OtpOperationController"/>.
///
/// TESTING STRATEGY:
/// Services are mocked with Moq via <see cref="WebApiFactory"/> so tests cover:
///   - Correct HTTP method and route matching
///   - Correct status code mapping from service response to HTTP response
///   - Request body deserialization and forwarding to service
///   - Exception handling returning 500
///   - No database, SMTP, or external service needed
///
/// Testcontainers is NOT needed here — the service layer is the
/// trust boundary. Database-level behaviour is covered by the
/// service integration tests.
/// </summary>
public sealed class AuthenticationAndUserControllerTests : IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private WebApiFactory _factory = null!;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // IAsyncLifetime
    // -------------------------------------------------------------------------

    public Task InitializeAsync()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();

        _factory = new WebApiFactory();
        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static LoginUserDto BuildLoginDto(
        string userNameOrEmail = "user@test.com",
        string password = "Test@1234!") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail,
            Password = password
        };

    private static CreateUserDto BuildCreateUserDto(
        string? email = null,
        string? userName = null) => new()
        {
            FirstName = "John",
            LastName = "Doe",
            EmailAddress = email ?? "user@test.com",
            UserName = userName ?? "johndoe",
            Password = "Testedok@1234!",
            ConfirmPassword = "Testedok@1234!"
        };

    private static UserDeactivationRequestDto BuildDeactivationDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail,
            DeactivationJustification = "Test deactivation."
        };

    private static ReactivateUserRequestDto BuildReactivationDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail
        };

    private static RequestOtpDto BuildRequestOtpDto(
        string userNameOrEmail = "user@test.com") => new()
        {
            UserNameOrEmailAddress = userNameOrEmail,
            Purpose = 0
        };

    private static ChangePasswordDto BuildChangePasswordDto() => new()
    {
        UserNameOrEmailAddress = "user@test.com",
        OldPassword = "OldPass@1234!",
        NewPassword = "NewPass@1234!",
        ConfirmNewPassword = "NewPass@1234!"
    };

    private static OtpVerificationRequestDto BuildOtpVerificationDto(
        string? usereamiladdress = null,
        string otp = "123456") => new()
        {
            EmailAddress = usereamiladdress ?? "",
            Otp = otp
        };

    // =========================================================================
    // AuthenticationController — POST /api/Authentication/login
    // =========================================================================

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithTokens()
    {
        // Arrange
        var tokenDto = new TokenDto("access-token-value", "refresh-token-value");

        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Success(
                tokenDto, "User signed in successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("access-token-value", body.ResponseData!.accessToken);
        Assert.Equal("refresh-token-value", body.ResponseData.refreshToken);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns404()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "Invalid Credentials.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<TokenDto>>();
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task Login_AccountLockedOut_Returns400()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(
                null, "User profiled locked out.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Login_ForwardsCredentialsToService()
    {
        // Arrange
        var capturedDto = null as LoginUserDto;

        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<LoginUserDto, CancellationToken>((dto, _) => capturedDto = dto)
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(null, "Not found.", HttpStatusCode.NotFound));

        var loginDto = BuildLoginDto("specificuser@test.com", "SpecificPass@1!");

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/login", loginDto);

        // Assert — correct values forwarded
        Assert.NotNull(capturedDto);
        Assert.Equal("specificuser@test.com", capturedDto!.UserNameOrEmailAddress);
        Assert.Equal("SpecificPass@1!", capturedDto.Password);
    }

    [Fact]
    public async Task Login_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<TokenDto>.Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.PostAsJsonAsync("/api/Authentication/login", BuildLoginDto());

        // Assert
        _factory.AuthenticationServiceMock.Verify(
            s => s.LoginUserAsync(It.IsAny<LoginUserDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // AuthenticationController — POST /api/Authentication/change-password
    // =========================================================================

    [Fact]
    public async Task ChangePassword_ValidRequest_Returns200()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.ChangePasswordAsync(It.IsAny<ChangePasswordDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/change-password", BuildChangePasswordDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // AuthenticationController — POST /api/Authentication/request-otp
    // =========================================================================

    [Fact]
    public async Task RequestOtp_ValidRequest_Returns200()
    {
        // Arrange
        _factory.AuthenticationServiceMock
            .Setup(s => s.RequestOtpAsync(It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.RequestOtpAsync(It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.RequestOtpAsync(It.IsAny<RequestOtpDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/Authentication/request-otp", BuildRequestOtpDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // UsersController — POST /api/Users/register
    // =========================================================================

    [Fact]
    public async Task Register_ValidRequest_Returns201Created()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "User created successfully.", HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409Conflict()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "User with email already exists.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateUsername_Returns409Conflict()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Username already taken.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400BadRequest()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "Your profile could not be created.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync("/api/Users/register", BuildCreateUserDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Register_ForwardsRequestBodyToService()
    {
        // Arrange
        var capturedDto = null as CreateUserDto;

        _factory.UserServiceMock
            .Setup(s => s.CreateUserAsync(It.IsAny<CreateUserDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateUserDto, CancellationToken>((dto, _) => capturedDto = dto)
            .ReturnsAsync(GenericResponse<string>.Success("OK", "Created.", HttpStatusCode.Created));

        var request = BuildCreateUserDto("specific@test.com", "specificuser");

        // Act
        await _client.PostAsJsonAsync("/api/Users/register", request);

        // Assert
        Assert.NotNull(capturedDto);
        Assert.Equal("specific@test.com", capturedDto!.EmailAddress);
        Assert.Equal("specificuser", capturedDto.UserName);
    }

    // =========================================================================
    // UsersController — POST /api/Users/deactivate
    // =========================================================================

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

    // =========================================================================
    // UsersController — POST /api/Users/reactivate
    // =========================================================================

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

    // =========================================================================
    // OtpOperationController — POST /api/OtpOperation/validate-otp
    // =========================================================================

    [Fact]
    public async Task ValidateOtp_ValidCode_Returns200()
    {
        // Arrange
        //_factory.OtpServiceMock
        //    .Setup(s => s.ValidateOtpAsync(
        //        It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
        //    .ReturnsAsync(GenericResponse<string>.Success(
        //        "OK", "OTP validated successfully.", HttpStatusCode.OK));
        _factory.OtpServiceMock.Setup(o => o.ValidateOtpAsync(It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Success(
                new OtpVerificationDto() { ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30), SignedToken = RandomNumberGenerator.GetHexString(12) },
                "OTP validated successfully.",
                HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidateOtp_InvalidCode_Returns400()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "OTP is invalid.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto(otp: "000000"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidateOtp_ExpiredCode_Returns410Gone()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "OTP has expired.", HttpStatusCode.Gone));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task ValidateOtp_NoActiveOtp_Returns404()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "No active OTP found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ValidateOtp_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // OtpOperationController — DELETE /api/OtpOperation/revoke-otp/{userId}
    // =========================================================================

    [Fact]
    public async Task RevokeOtp_ValidUserId_Returns200()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "OTP revoked successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_NoActiveOtp_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Failure(
                null, "No active OTP found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_ForwardsUserIdToService()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>.Success("OK", "Revoked.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync($"/api/OtpOperation/revoke-otp/{userId}");

        // Assert — correct userId routed to service
        Assert.Equal(userId, capturedId);
    }

    [Fact]
    public async Task RevokeOtp_InvalidGuidInRoute_Returns400()
    {
        // Arrange — route constraint {userId:guid} rejects non-GUID values

        // Act
        var response = await _client.DeleteAsync(
            "/api/OtpOperation/revoke-otp/12345");

        // Assert — ASP.NET Core route constraint rejects before hitting controller
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_ServiceThrowsException_Returns500()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
