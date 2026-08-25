using Moq;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.OtpOperation;
using WebHook.Core.Interfaces.Services;
using WebHook.IntegrationTests.Controllers;

namespace WebHook.IntegrationTests.Controllers.Otp;

/// <summary>
/// HTTP-level integration tests for <see cref="OtpOperationController"/>.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>POST   api/OtpOperation/validate-otp         — ValidateOtp  [AllowAnonymous]</description></item>
///   <item><description>DELETE api/OtpOperation/revoke-otp/{userId}  — RevokeOtp    [Authorize(Roles="Admin")]</description></item>
/// </list>
/// </summary>
public sealed class OtpOperationControllerIntegrationTests
    : IClassFixture<WebApiFactory>, IAsyncLifetime
{
    private readonly WebApiFactory _factory;
    private HttpClient _client = null!;

    public OtpOperationControllerIntegrationTests(WebApiFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        // Bearer header for the protected RevokeOtp endpoint
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

    private static OtpVerificationRequestDto BuildOtpVerificationDto(
        string? emailAddress = null,
        string  otp          = "123456") => new()
        {
            EmailAddress = emailAddress ?? "user@test.com",
            Otp          = otp
        };

    // =========================================================================
    // POST api/OtpOperation/validate-otp — [AllowAnonymous]
    // =========================================================================

    [Fact]
    public async Task ValidateOtp_NoAuthToken_StillReachesController()
    {
        // Arrange — public endpoint
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "Invalid OTP.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert — public so must not return 401
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ValidateOtp_ValidCode_Returns200()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Success(
                new OtpVerificationDto
                {
                    ExpiresAt   = DateTimeOffset.UtcNow.AddSeconds(30),
                    SignedToken = RandomNumberGenerator.GetHexString(12)
                },
                "OTP validated successfully.",
                HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<OtpVerificationDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
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
    public async Task ValidateOtp_ForwardsRequestBodyToService()
    {
        // Arrange
        OtpVerificationRequestDto? captured = null;
        var request = BuildOtpVerificationDto("specific@test.com", "654321");

        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .Callback<OtpVerificationRequestDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "Invalid.", HttpStatusCode.BadRequest));

        // Act
        await _client.PostAsJsonAsync("/api/OtpOperation/validate-otp", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("specific@test.com", captured!.EmailAddress);
        Assert.Equal("654321",            captured.Otp);
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

    [Fact]
    public async Task ValidateOtp_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.OtpServiceMock
            .Setup(s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<OtpVerificationDto>.Failure(
                null, "Invalid.", HttpStatusCode.BadRequest));

        // Act
        await _client.PostAsJsonAsync(
            "/api/OtpOperation/validate-otp", BuildOtpVerificationDto());

        // Assert
        _factory.OtpServiceMock.Verify(
            s => s.ValidateOtpAsync(
                It.IsAny<OtpVerificationRequestDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // DELETE api/OtpOperation/revoke-otp/{userId:guid} — [Authorize(Roles="Admin")]
    // =========================================================================

    [Fact]
    public async Task RevokeOtp_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_ValidUserId_Returns200()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "OTP revoked successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task RevokeOtp_NoActiveOtp_Returns404()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(
                userId, It.IsAny<CancellationToken>()))
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
        var userId     = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Revoked.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync($"/api/OtpOperation/revoke-otp/{userId}");

        // Assert — correct userId routed to service
        Assert.Equal(userId, capturedId);
    }

    [Fact]
    public async Task RevokeOtp_NonGuidInRoute_Returns404()
    {
        // Route constraint {userId:guid} causes ASP.NET Core to return 404
        // when the value is not a valid GUID — no matching route found
        var response = await _client.DeleteAsync(
            "/api/OtpOperation/revoke-otp/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_ServiceThrowsException_Returns500()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(
                userId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task RevokeOtp_ServiceCalledExactlyOnce()
    {
        // Arrange
        var userId = Guid.NewGuid();

        _factory.OtpServiceMock
            .Setup(s => s.RevokeUserOtpAsync(
                userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>.Success(
                "OK", "Revoked.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync($"/api/OtpOperation/revoke-otp/{userId}");

        // Assert
        _factory.OtpServiceMock.Verify(
            s => s.RevokeUserOtpAsync(
                userId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
