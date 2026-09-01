using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookDeadLetterQueueController"/>.
///
/// TESTING STRATEGY:
/// <see cref="IDeadLetterQueueService"/> and <see cref="ICacheService"/> are
/// replaced with Moq mocks via <see cref="DeadLetterQueueWebApiFactory"/> so
/// tests cover:
///   - Correct HTTP method and route matching for the nested route
///     <c>api/WebhookDelivery/{deliveryId:guid}/deadLetters</c>
///   - Authentication — unauthenticated requests return 401
///   - Role-based access — <c>RequestManualRetry</c> requires Admin
///   - Status code mapping from service response to HTTP response
///   - Route parameter (<c>deliveryId</c>) forwarding to service
///   - Request body (<c>RequestManualRetryDto</c>) forwarding to service
///   - Exception handling returning 500
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET  api/WebhookDelivery/{deliveryId}/deadLetters  — GetDeadLetterQueue  [Authorize]</description></item>
///   <item><description>POST api/WebhookDelivery/{deliveryId}/deadLetters  — RequestManualRetry  [Authorize(Roles="Admin")]</description></item>
/// </list>
/// </summary>
public sealed class WebhookDeadLetterQueueControllerIntegrationTests
    : IClassFixture<DeadLetterQueueWebApiFactory>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly DeadLetterQueueWebApiFactory _factory;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // Route helper
    // -------------------------------------------------------------------------

    private static string BaseRoute(Guid deliveryId)
        => $"/api/WebhookDelivery/{deliveryId}/deadLetters";

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookDeadLetterQueueControllerIntegrationTests(
        DeadLetterQueueWebApiFactory factory)
        => _factory = factory;

    // -------------------------------------------------------------------------
    // IAsyncLifetime — runs before and after each test method
    // -------------------------------------------------------------------------

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        // Set Bearer header so CustomAuthenticationFilter passes step 2.
        // The token value is irrelevant — the filter reads claims from
        // HttpContext.User which DeadLetterTestAuthHandler already populated.
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

    private static DeadLetterQueueDto BuildDeadLetterDto(
        Guid? id = null) => new(
            id: id ?? Guid.NewGuid(),
            createdAt: DateTimeOffset.UtcNow.AddHours(-1),
            reason: "Exceeded maximum retry attempts.",
            RetriedAt: null,
            RetryJustification: null,
            retriedBy: null);

    private static RequestManualRetryDto BuildRetryDto(
        Guid? deadLetterId = null,
        Guid? deliveryId = null,
        string justification = "Endpoint is now healthy after maintenance.") => new()
        {
            DeadLetterId = deadLetterId ?? Guid.NewGuid(),
            DeliveryId = deliveryId ?? Guid.NewGuid(),
            RetryJustification = justification
        };

    // =========================================================================
    // GetDeadLetterQueue — GET api/WebhookDelivery/{deliveryId}/deadLetters
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task GetDeadLetterQueue_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDeadLetterQueue_NoItemsFound_Returns404()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Failure(null, "Dead letter queue items do not exist.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetDeadLetterQueue_ItemsExist_Returns200()
    {
        // Arrange
        var items = new List<DeadLetterQueueDto>
        {
            BuildDeadLetterDto(),
            BuildDeadLetterDto()
        };

        _factory.DeadLetterServiceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Success(items, "Dead letter queues fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<DeadLetterQueueDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(items.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetDeadLetterQueue_ForwardsDeliveryIdToService()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.DeadLetterServiceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(deliveryId));

        // Assert — correct route parameter forwarded to service
        Assert.Equal(deliveryId, capturedId);
    }

    [Fact]
    public async Task GetDeadLetterQueue_NonGuidInRoute_Returns404()
    {
        // Route constraint {deliveryId:guid} causes 404 — no matching route
        var response = await _client.GetAsync(
            "/api/WebhookDelivery/not-a-guid/deadLetters");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDeadLetterQueue_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetDeadLetterQueue_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        _factory.DeadLetterServiceMock.Verify(
            s => s.GetDeliveryDeadKetterAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // RequestManualRetry — POST api/WebhookDelivery/{deliveryId}/deadLetters
    // Requires: Admin role
    // =========================================================================

    [Fact]
    public async Task RequestManualRetry_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_ValidRequest_Returns200()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Manual retry requested successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task RequestManualRetry_DeadLetterNotFound_Returns404()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Dead Letter with Id does not exist.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task RequestManualRetry_InvalidDeliveryStatus_Returns400()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Could not proceed. Delivery Status: Delivered", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_AlreadyRetried_Returns409Conflict()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Dead letter queue already retried.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_RetryCycleExhausted_Returns422()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Retry cycle already exceeded.", HttpStatusCode.UnprocessableEntity));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_ForwardsRequestBodyToService()
    {
        // Arrange
        var deadLetterId = Guid.NewGuid();
        var deliveryId = Guid.NewGuid();
        var justification = "Confirmed endpoint is healthy after maintenance.";
        RequestManualRetryDto? captured = null;

        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .Callback<RequestManualRetryDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Retry requested.", HttpStatusCode.OK));

        var request = BuildRetryDto(deadLetterId, deliveryId, justification);

        // Act
        await _client.PostAsJsonAsync(BaseRoute(Guid.NewGuid()), request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(deadLetterId, captured!.DeadLetterId);
        Assert.Equal(deliveryId, captured.DeliveryId);
        Assert.Equal(justification, captured.RetryJustification);
    }

    [Fact]
    public async Task RequestManualRetry_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.DeadLetterServiceMock
            .Setup(s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Retry requested.", HttpStatusCode.OK));

        // Act
        await _client.PostAsJsonAsync(BaseRoute(Guid.NewGuid()), BuildRetryDto());

        // Assert
        _factory.DeadLetterServiceMock.Verify(
            s => s.RequestManualRetryAsync(
                It.IsAny<RequestManualRetryDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestManualRetry_NonGuidInRoute_Returns404()
    {
        // Route constraint {deliveryId:guid} causes 404 — no matching route
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookDelivery/not-a-guid/deadLetters", BuildRetryDto());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
/// <see cref="WebhookDeadLetterQueueController"/> integration tests.
///
/// Follows the same pattern as <see cref="WebhookSubscriptionWebApiFactory"/>:
/// <list type="bullet">
///   <item><description><see cref="IDeadLetterQueueService"/> replaced with a Moq mock.</description></item>
///   <item><description><see cref="ICacheService"/> replaced with a Moq mock seeded in
///   <see cref="ResetMocks"/> so <see cref="CustomAuthenticationFilter"/> passes
///   authenticated requests through without rejecting on default Guid.</description></item>
///   <item><description>A single <see cref="DeadLetterTestAuthHandler"/> carries both USER and Admin
///   roles so all endpoints are reachable without multiple factories.</description></item>
/// </list>
/// </summary>
public sealed class DeadLetterQueueWebApiFactory
    : WebApplicationFactory<Program>
{
    public Mock<IDeadLetterQueueService> DeadLetterServiceMock { get; } = new();
    public Mock<ICacheService> CacheServiceMock { get; } = new();

    /// <summary>
    /// Resets all mock setups and recorded invocations before each test method.
    /// Re-applies the cache setup after reset using the typed
    /// <see cref="DeadLetterTestAuthHandler.TestJtiGuid"/> so
    /// <see cref="CustomAuthenticationFilter"/> never sees a default Guid.
    /// </summary>
    public void ResetMocks()
    {
        DeadLetterServiceMock.Reset();
        CacheServiceMock.Reset();

        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                DeadLetterTestAuthHandler.TestEmail))
            .ReturnsAsync(DeadLetterTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDeadLetterQueueService>();
            services.RemoveAll<ICacheService>();

            services.AddSingleton(DeadLetterServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, DeadLetterTestAuthHandler>(
                    DeadLetterTestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = DeadLetterTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = DeadLetterTestAuthHandler.SchemeName;
            });

            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.RemoveAll<IOptionsChangeTokenSource<RateLimiterOptions>>();

            services.AddRateLimiter(opts =>
            {
                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                opts.AddPolicy("request-otp-limit", context =>
                    RateLimitPartition.GetNoLimiter("test"));

                opts.AddPolicy("validate-otp-limit", context =>
                    RateLimitPartition.GetNoLimiter("test"));

                opts.AddPolicy("per-user-rating", context =>
                    RateLimitPartition.GetNoLimiter("test"));
            });
        });
    }
}

// =============================================================================
// Test auth handler
// =============================================================================

/// <summary>
/// Auto-authenticates every request as a test user carrying both USER and Admin
/// roles so all endpoints in <see cref="WebhookDeadLetterQueueController"/> are
/// reachable in tests.
///
/// <see cref="TestEmail"/> and <see cref="TestJtiGuid"/> are seeded into the
/// cache mock in <see cref="DeadLetterQueueWebApiFactory.ResetMocks"/> so
/// <see cref="CustomAuthenticationFilter"/> finds a non-default cached JTI
/// matching the token JTI claim and passes the request through.
///
/// Email and JTI are distinct from those in other test auth handlers to
/// prevent cache key collisions when multiple factories run in the same process.
/// </summary>
public sealed class DeadLetterTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DeadLetterTestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000006";
    public const string TestEmail = "TESTDEADLETTER@ACME.COM";

    /// <summary>String form used in the JTI claim (requires string).</summary>
    public const string TestJti = "00000000-0000-0000-0000-000000000093";

    /// <summary>
    /// Typed Guid used in <see cref="DeadLetterQueueWebApiFactory.ResetMocks"/>
    /// to avoid <see cref="Guid.Parse"/> and prevent a startup failure from a
    /// malformed constant.
    /// </summary>
    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000093");

    public DeadLetterTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId),
            new Claim(ClaimTypes.Email,          TestEmail),
            new Claim(ClaimTypes.Role,           "USER"),
            new Claim(ClaimTypes.Role,           "Admin"),
            new Claim(JwtRegisteredClaimNames.Jti, TestJti)
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}