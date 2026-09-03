using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
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
using WebHook.Api.ApplicationFilters;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookEventController"/>.
///
/// TESTING STRATEGY:
/// <see cref="IWebhookEventService"/> and <see cref="ICacheService"/> are
/// replaced with Moq mocks via <see cref="WebhookEventWebApiFactory"/> so
/// tests cover:
///   - Correct HTTP method and route matching
///   - Authentication — unauthenticated requests to protected endpoints return 401
///   - Status code mapping from service response to HTTP response
///   - Route and query parameter forwarding to service
///   - Request body forwarding to service
///   - Exception handling returning 500
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET  api/webhookevent/{correlationId:guid}  — GetEventByCorrelationId  [Authorize]</description></item>
///   <item><description>GET  api/webhookevent                       — GetAllEvents             [Authorize(Roles="Admin")]</description></item>
///   <item><description>POST api/webhookevent                       — CreateEvent              [AllowAnonymous]</description></item>
/// </list>
/// </summary>
public sealed class WebhookEventControllerIntegrationTests
    : IClassFixture<WebhookEventWebApiFactory>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WebhookEventWebApiFactory _factory;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookEventControllerIntegrationTests(WebhookEventWebApiFactory factory)
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
        // HttpContext.User which WebhookEventTestAuthHandler already populated.
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

    private static WebhookEventDto BuildEventDto(
        Guid?               correlationId = null,
        string              eventType     = "CustomerCreated",
        WebHookEventStatus? status        = null) => new()
        {
            Id            = Guid.NewGuid(),
            EventType     = eventType,
            PayLoad       = "{\"customerId\":\"12345\"}",
            Source        = "TestSource",
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Status        = (status ?? WebHookEventStatus.Pending).ToString(),
            CreatedAt     = DateTimeOffset.UtcNow
        };

    private static CreateWebhookEventDto BuildCreateDto(
        string eventType = "CustomerCreated",
        string payload   = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}",
        string source    = "TestSource") => new()
        {
            EventType     = eventType,
            PayLoad       = payload,
            Source        = source,
            CorrelationId = Guid.NewGuid()
        };

    private static string EventQueryRoute(
        string? eventType     = "CustomerCreated",
        string? source        = "TestSource",
        string? status        = null,
        Guid?   correlationId = null)
    {
        var qs = $"?eventType={eventType}&source={source}";
        if (status        is not null) qs += $"&status={status}";
        if (correlationId is not null) qs += $"&correlationId={correlationId}";
        return $"/api/webhookevent{qs}";
    }

    // =========================================================================
    // GetEventByCorrelationId — GET api/webhookevent/{correlationId:guid}
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task GetEventByCorrelationId_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(
            $"/api/webhookevent/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEventByCorrelationId_EventNotFound_Returns404()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Failure(null, "Webhook event not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(
            $"/api/webhookevent/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookEventDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetEventByCorrelationId_EventExists_Returns200()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var events        = new List<WebhookEventDto> { BuildEventDto(correlationId) };

        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Success(events, "Webhook event fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(
            $"/api/webhookevent/{correlationId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookEventDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Single(body.ResponseData);
    }

    [Fact]
    public async Task GetEventByCorrelationId_ForwardsCorrelationIdToService()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var capturedId    = Guid.Empty;

        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync($"/api/webhookevent/{correlationId}");

        // Assert
        Assert.Equal(correlationId, capturedId);
    }

    [Fact]
    public async Task GetEventByCorrelationId_NonGuidInRoute_Returns404()
    {
        // Route constraint {correlationId:guid} causes 404 — no matching route
        var response = await _client.GetAsync("/api/webhookevent/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEventByCorrelationId_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(
            $"/api/webhookevent/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetEventByCorrelationId_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync($"/api/webhookevent/{Guid.NewGuid()}");

        // Assert
        _factory.EventServiceMock.Verify(
            s => s.GetWebhookEventAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // GetAllEvents — GET api/webhookevent?{queryParams}
    // Requires: Admin role
    // =========================================================================

    [Fact]
    public async Task GetAllEvents_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(EventQueryRoute());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllEvents_EventsExist_Returns200()
    {
        // Arrange
        var events = new List<WebhookEventDto>
        {
            BuildEventDto(eventType: "CustomerCreated"),
            BuildEventDto(eventType: "OrderCreated")
        };

        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Success(events, "Events fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(EventQueryRoute());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookEventDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(events.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetAllEvents_NoEventsFound_Returns200WithEmptyList()
    {
        // Arrange — GetAllEvents always returns 200 even when result set is empty
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Success(new List<WebhookEventDto>(), "No events found.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(EventQueryRoute());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookEventDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Empty(body.ResponseData);
    }

    [Fact]
    public async Task GetAllEvents_ForwardsQueryParamsToService()
    {
        // Arrange
        GetWebhookEventParameters? captured  = null;
        var correlationId                    = Guid.NewGuid();

        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()))
            .Callback<GetWebhookEventParameters, CancellationToken>(
                (p, _) => captured = p)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Success([], "OK.", HttpStatusCode.OK));

        // Act
        await _client.GetAsync(EventQueryRoute(
            eventType:     "OrderCreated",
            source:        "OrderService",
            status:        "Pending",
            correlationId: correlationId));

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("OrderCreated",        captured!.EventType);
        Assert.Equal("OrderService",        captured.Source);
        Assert.Equal("Pending",             captured.Status);
        Assert.Equal(correlationId,         captured.CorrelationId);
    }

    [Fact]
    public async Task GetAllEvents_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(EventQueryRoute());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAllEvents_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookEventDto>>
                .Success([], "OK.", HttpStatusCode.OK));

        // Act
        await _client.GetAsync(EventQueryRoute());

        // Assert
        _factory.EventServiceMock.Verify(
            s => s.GetWebhookEventsAsync(
                It.IsAny<GetWebhookEventParameters>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // CreateEvent — POST api/webhookevent
    // No [Authorize] — public endpoint
    // =========================================================================

    [Fact]
    public async Task CreateEvent_NoAuthToken_ReachesController()
    {
        // Arrange — public endpoint, no auth required
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Invalid payload.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto());

        // Assert — endpoint is public so request should reach the controller
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_ValidEvent_Returns201Created()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Webhook event created successfully.", HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task CreateEvent_DuplicateCorrelationId_Returns409Conflict()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Correlation Id already exists.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_InvalidEventType_Returns400()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Invalid event type.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto(eventType: "InvalidEventType"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_InvalidPayload_Returns400()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Invalid payload for event type.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto(payload: "InvalidPayload"));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_ForwardsRequestBodyToService()
    {
        // Arrange
        CreateWebhookEventDto? captured = null;
        var request = BuildCreateDto("OrderCreated", "{\"orderId\":\"abc\"}", "OrderService");

        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWebhookEventDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/webhookevent", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(request.EventType,     captured!.EventType);
        Assert.Equal(request.PayLoad,       captured.PayLoad);
        Assert.Equal(request.Source,        captured.Source);
        Assert.Equal(request.CorrelationId, captured.CorrelationId);
    }

    [Fact]
    public async Task CreateEvent_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/webhookevent", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.EventServiceMock
            .Setup(s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/webhookevent", BuildCreateDto());

        // Assert
        _factory.EventServiceMock.Verify(
            s => s.CreateEventAsync(
                It.IsAny<CreateWebhookEventDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
/// <see cref="WebhookEventController"/> integration tests.
/// </summary>
public sealed class WebhookEventWebApiFactory
    : WebApplicationFactory<Program>
{
    public Mock<IWebhookEventService> EventServiceMock { get; } = new();
    public Mock<ICacheService> CacheServiceMock { get; } = new();

    public void ResetMocks()
    {
        EventServiceMock.Reset();
        CacheServiceMock.Reset();

        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                WebhookEventTestAuthHandler.TestEmail))
            .ReturnsAsync(WebhookEventTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebhookEventService>();
            services.RemoveAll<ICacheService>();
            services.RemoveAll<ClientValidationFilter>(); // add

            services.AddSingleton(EventServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            // Replace real filter with no-op — OnAuthorizationAsync always passes
            services.AddScoped<ClientValidationFilter>(sp =>
                new NoOpClientValidationFilter(
                    sp.GetRequiredService<RepositoryContext>(),
                    sp.GetRequiredService<IApplicationHasher>()));

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, WebhookEventTestAuthHandler>(
                    WebhookEventTestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = WebhookEventTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = WebhookEventTestAuthHandler.SchemeName;
            });

            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.RemoveAll<IOptionsChangeTokenSource<RateLimiterOptions>>();

            services.AddRateLimiter(opts =>
            {
                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                opts.AddPolicy("request-otp-limit", _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("validate-otp-limit", _ => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("per-user-rating", _ => RateLimitPartition.GetNoLimiter("test"));
            });
        });
    }
}

// =============================================================================
// Test auth handler
// =============================================================================

/// <summary>
/// Auto-authenticates every request as a test user carrying both USER and Admin
/// roles so all protected endpoints in <see cref="WebhookEventController"/> are
/// reachable in tests.
/// </summary>
public sealed class WebhookEventTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "WebhookEventTestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000005";
    public const string TestEmail  = "TESTWEBHOOKEVENT@ACME.COM";
    public const string TestJti    = "00000000-0000-0000-0000-000000000094";
    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000094");

    public WebhookEventTestAuthHandler(
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

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class NoOpClientValidationFilter : ClientValidationFilter
{
    public NoOpClientValidationFilter(
        RepositoryContext ctx,
        IApplicationHasher hasher) : base(ctx, hasher) { }

    public override Task OnAuthorizationAsync(AuthorizationFilterContext context)
        => Task.CompletedTask;
}