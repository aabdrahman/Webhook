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
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookEventCatalogController"/>.
///
/// TESTING STRATEGY:
/// <see cref="IWebhookEventCatalogService"/> and <see cref="ICacheService"/> are
/// replaced with Moq mocks via <see cref="EventCatalogWebApiFactory"/> so tests cover:
///   - Correct HTTP method and route matching
///   - Authentication and authorisation — unauthenticated requests return 401
///   - Status code mapping from service response to HTTP response
///   - Request body and route parameter forwarding to service
///   - Query parameter forwarding (<c>isDeactivate</c>) to service
///   - Exception handling returning 500
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET /api/WebhookEventCatalog                          — GetAllEventCatalog   [AllowAnonymous]</description></item>
///   <item><description>GET /api/WebhookEventCatalog/{id}                     — GetEventCatalogById  [Authorize]</description></item>
///   <item><description>POST /api/WebhookEventCatalog                         — CreateEventCatalog   [Authorize(Roles="Admin")]</description></item>
///   <item><description>PUT /api/WebhookEventCatalog/{id}?isDeactivate={bool} — ActivationAction     [Authorize(Roles="Admin")]</description></item>
/// </list>
/// </summary>
public sealed class WebhookEventCatalogControllerIntegrationTests
    : IClassFixture<EventCatalogWebApiFactory>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly EventCatalogWebApiFactory _factory;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookEventCatalogControllerIntegrationTests(
        EventCatalogWebApiFactory factory)
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
        // HttpContext.User which EventCatalogTestAuthHandler already populated.
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

    private static EventCatalogDto BuildEventCatalogDto(
        string name = "CustomerCreated") => new()
        {
            Id               = Guid.NewGuid(),
            EventCatalogName = name.ToUpperInvariant(),
            Description      = $"{name} description.",
            IsActive         = true,
            AvailableFields  = new Dictionary<string, string>
            {
                { "name",  "string" },
                { "email", "string" }
            }
        };

    private static CreateEventCatalogDto BuildCreateDto(
        string name = "OrderCreated") => new()
        {
            EventCatalogName = name,
            Description      = $"{name} description.",
            AvailableFields  = new Dictionary<string, string>
            {
                { "referenceNumber", "string" },
                { "count",           "int"    }
            }
        };

    // =========================================================================
    // GetAllEventCatalog — GET /api/WebhookEventCatalog
    // No [Authorize] — public endpoint
    // =========================================================================

    [Fact]
    public async Task GetAllEventCatalog_NoAuthToken_Returns200()
    {
        // Arrange — public endpoint so no auth header needed
        _client.DefaultRequestHeaders.Authorization = null;

        _factory.EventCatalogServiceMock
            .Setup(s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<EventCatalogDto>>
                .Failure(null, "No event catalog found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync("/api/WebhookEventCatalog");

        // Assert — endpoint is public so request should reach the controller
        // (404 from service means it reached the controller, not a 401)
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllEventCatalog_NoEventCatalogs_Returns404()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<EventCatalogDto>>
                .Failure(null, "No event catalog found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync("/api/WebhookEventCatalog");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<EventCatalogDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetAllEventCatalog_EventCatalogsExist_Returns200()
    {
        // Arrange
        var catalogs = new List<EventCatalogDto>
        {
            BuildEventCatalogDto("CustomerCreated"),
            BuildEventCatalogDto("OrderCreated"),
            BuildEventCatalogDto("PaymentReceived")
        };

        _factory.EventCatalogServiceMock
            .Setup(s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<EventCatalogDto>>
                .Success(catalogs, "Event catalogs fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync("/api/WebhookEventCatalog");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<EventCatalogDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(catalogs.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetAllEventCatalog_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync("/api/WebhookEventCatalog");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAllEventCatalog_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<EventCatalogDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync("/api/WebhookEventCatalog");

        // Assert
        _factory.EventCatalogServiceMock.Verify(
            s => s.GetAllEventCatalogAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // GetEventCatalogById — GET /api/WebhookEventCatalog/{EventCatalogId}
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task GetEventCatalogById_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEventCatalogById_NonExistingId_Returns404()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.GetEventCatalogByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<EventCatalogDto>
                .Failure(null, "Event catalog not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<EventCatalogDto>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetEventCatalogById_ExistingId_Returns200()
    {
        // Arrange
        var catalog = BuildEventCatalogDto("CustomerCreated");

        _factory.EventCatalogServiceMock
            .Setup(s => s.GetEventCatalogByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<EventCatalogDto>
                .Success(catalog, "Event catalog fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookEventCatalog/{catalog.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<EventCatalogDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(catalog.EventCatalogName, body.ResponseData.EventCatalogName);
    }

    [Fact]
    public async Task GetEventCatalogById_ForwardsIdToService()
    {
        // Arrange
        var catalogId  = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.EventCatalogServiceMock
            .Setup(s => s.GetEventCatalogByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<EventCatalogDto>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync($"/api/WebhookEventCatalog/{catalogId}");

        // Assert — correct route parameter forwarded to service
        Assert.Equal(catalogId, capturedId);
    }

    [Fact]
    public async Task GetEventCatalogById_NonGuidInRoute_Returns404()
    {
        // Route constraint {EventCatalogId:guid} causes ASP.NET Core
        // to return 404 when the value does not match — no route found
        var response = await _client.GetAsync(
            "/api/WebhookEventCatalog/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEventCatalogById_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.GetEventCatalogByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // CreateEventCatalog — POST /api/WebhookEventCatalog
    // Requires: Admin role
    // =========================================================================

    [Fact]
    public async Task CreateEventCatalog_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/WebhookEventCatalog", BuildCreateDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateEventCatalog_ValidRequest_Returns201Created()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Event catalog created successfully.", HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookEventCatalog", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task CreateEventCatalog_DuplicateEntry_Returns409Conflict()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event catalog already exists.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookEventCatalog", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task CreateEventCatalog_InvalidRequest_Returns400()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Invalid request.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookEventCatalog", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEventCatalog_ForwardsRequestBodyToService()
    {
        // Arrange
        CreateEventCatalogDto? captured = null;
        var request = BuildCreateDto("PaymentReceived");

        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateEventCatalogDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/WebhookEventCatalog", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(request.EventCatalogName, captured!.EventCatalogName);
        Assert.Equal(request.Description,      captured.Description);
        Assert.Equal(request.AvailableFields.Count, captured.AvailableFields.Count);
    }

    [Fact]
    public async Task CreateEventCatalog_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookEventCatalog", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task CreateEventCatalog_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/WebhookEventCatalog", BuildCreateDto());

        // Assert
        _factory.EventCatalogServiceMock.Verify(
            s => s.CreateNewEventCatalogAsync(
                It.IsAny<CreateEventCatalogDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // ActivationAction — PUT /api/WebhookEventCatalog/{EventCatalogId}?isDeactivate={bool}
    // Requires: Admin role
    // Default: isDeactivate = true
    // =========================================================================

    [Fact]
    public async Task ActivationAction_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActivationAction_Deactivate_NonExistingId_Returns404()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event catalog not found.", HttpStatusCode.NotFound));

        // Act — isDeactivate defaults to true
        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivationAction_Deactivate_ValidId_Returns200()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Event catalog deactivated successfully.", HttpStatusCode.OK));

        // Act — isDeactivate=true (default)
        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}?isDeactivate=true", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task ActivationAction_Activate_ValidId_Returns200()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Event catalog activated successfully.", HttpStatusCode.OK));

        // Act — isDeactivate=false activates the catalog
        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}?isDeactivate=false", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task ActivationAction_DefaultIsDeactivateIsTrue()
    {
        // Arrange — verify the default query param value is forwarded correctly
        bool? capturedIsDeactivate = null;

        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, bool, CancellationToken>((_, flag, _) => capturedIsDeactivate = flag)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "OK.", HttpStatusCode.OK));

        // Act — no isDeactivate query param — should default to true
        await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}", null);

        // Assert
        Assert.True(capturedIsDeactivate);
    }

    [Fact]
    public async Task ActivationAction_ForwardsIdToService()
    {
        // Arrange
        var catalogId  = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, bool, CancellationToken>((id, _, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "OK.", HttpStatusCode.OK));

        // Act
        await _client.PutAsync(
            $"/api/WebhookEventCatalog/{catalogId}", null);

        // Assert
        Assert.Equal(catalogId, capturedId);
    }

    [Fact]
    public async Task ActivationAction_AlreadyInRequestedState_Returns409Conflict()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Catalog is already in the requested state.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ActivationAction_NonGuidInRoute_Returns404()
    {
        // Route constraint {EventCatalogId:guid} causes 404 when value
        // does not match — ASP.NET Core finds no matching route
        var response = await _client.PutAsync(
            "/api/WebhookEventCatalog/not-a-guid", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivationAction_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.EventCatalogServiceMock
            .Setup(s => s.EventCatalogActivationAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookEventCatalog/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
/// <see cref="WebhookEventCatalogController"/> integration tests.
///
/// Follows the same pattern as <see cref="WebhookSubscriptionWebApiFactory"/>:
/// <list type="bullet">
///   <item><description><see cref="IWebhookEventCatalogService"/> replaced with a Moq mock.</description></item>
///   <item><description><see cref="ICacheService"/> replaced with a Moq mock seeded in
///   <see cref="ResetMocks"/> so <see cref="CustomAuthenticationFilter"/> passes
///   authenticated requests through without rejecting on default Guid.</description></item>
///   <item><description>A single <see cref="EventCatalogTestAuthHandler"/> carries both
///   USER and Admin roles so all endpoints are reachable.</description></item>
/// </list>
/// </summary>
public sealed class EventCatalogWebApiFactory
    : WebApplicationFactory<Program>
{
    public Mock<IWebhookEventCatalogService> EventCatalogServiceMock { get; } = new();
    public Mock<Core.Interfaces.Helpers.ICacheService>               CacheServiceMock        { get; } = new();

    /// <summary>
    /// Resets all mock setups and recorded invocations before each test method.
    /// Re-applies the cache setup after reset using the typed
    /// <see cref="EventCatalogTestAuthHandler.TestJtiGuid"/> so
    /// <see cref="CustomAuthenticationFilter"/> never sees a default Guid.
    /// </summary>
    public void ResetMocks()
    {
        EventCatalogServiceMock.Reset();
        CacheServiceMock.Reset();

        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                EventCatalogTestAuthHandler.TestEmail))
            .ReturnsAsync(EventCatalogTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebhookEventCatalogService>();
            services.RemoveAll<Core.Interfaces.Helpers.ICacheService>();

            services.AddSingleton(EventCatalogServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, EventCatalogTestAuthHandler>(
                    EventCatalogTestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = EventCatalogTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme    = EventCatalogTestAuthHandler.SchemeName;
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
/// roles so all endpoints in <see cref="WebhookEventCatalogController"/> are
/// reachable in tests.
///
/// <see cref="TestEmail"/> and <see cref="TestJtiGuid"/> are seeded into the
/// cache mock in <see cref="EventCatalogWebApiFactory.ResetMocks"/> so
/// <see cref="CustomAuthenticationFilter"/> finds a non-default cached JTI
/// matching the token JTI claim and passes the request through.
///
/// Email and JTI are distinct from those in other test auth handlers to
/// prevent cache key collisions when multiple factories run in the same process.
/// </summary>
public sealed class EventCatalogTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "EventCatalogTestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000003";
    public const string TestEmail  = "TESTEVENTCATALOG@ACME.COM";

    /// <summary>String form used in the JTI claim (requires string).</summary>
    public const string TestJti = "00000000-0000-0000-0000-000000000096";

    /// <summary>
    /// Typed Guid used in <see cref="EventCatalogWebApiFactory.ResetMocks"/>
    /// to avoid <see cref="Guid.Parse"/> and prevent a startup failure from a
    /// malformed constant.
    /// </summary>
    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000096");

    public EventCatalogTestAuthHandler(
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
