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
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookSubscriptionEventController"/>.
///
/// TESTING STRATEGY:
/// <see cref="IWebhookSubscriptionEventService"/> and <see cref="ICacheService"/> are
/// replaced with Moq mocks via <see cref="SubscriptionEventWebApiFactory"/> so tests cover:
///   - Correct HTTP method and route matching for the nested route
///     <c>api/WebhookSubscription/{subscriptionId:guid}/events</c>
///   - Authentication — unauthenticated requests return 401
///   - Status code mapping from service response to HTTP response
///   - Route parameter (<c>subscriptionId</c>) and query parameter
///     (<c>eventName</c>) forwarding to service
///   - Exception handling returning 500
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET    api/WebhookSubscription/{id}/events                        — GetSubscribedEvents  [Authorize]</description></item>
///   <item><description>PUT    api/WebhookSubscription/{id}/events?eventName={name}       — SubscribeEvent       [Authorize]</description></item>
///   <item><description>DELETE api/WebhookSubscription/{id}/events?eventName={name}       — UnsubscribeEvent     [Authorize]</description></item>
/// </list>
/// </summary>
public sealed class WebhookSubscriptionEventControllerIntegrationTests
    : IClassFixture<SubscriptionEventWebApiFactory>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly SubscriptionEventWebApiFactory _factory;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // Route helper — builds the base route for a given subscriptionId
    // -------------------------------------------------------------------------

    private static string BaseRoute(Guid subscriptionId)
        => $"/api/WebhookSubscription/{subscriptionId}/events";

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookSubscriptionEventControllerIntegrationTests(
        SubscriptionEventWebApiFactory factory)
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
        // HttpContext.User which SubscriptionEventTestAuthHandler already populated.
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

    private static WebhookSubscriptionEventDto BuildEventDto(
        string name = "OrderCreated") => new()
        {
            SubscriptionId   = Guid.NewGuid(),
            SubscriptionName = name
        };

    // =========================================================================
    // GetSubscribedEvents — GET api/WebhookSubscription/{subscriptionId}/events
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task GetSubscribedEvents_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedEvents_NoEventsFound_Returns404()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>
                .Failure(null, "No subscribed events found for the specified subscription.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetSubscribedEvents_EventsExist_Returns200()
    {
        // Arrange
        var events = new List<WebhookSubscriptionEventDto>
        {
            BuildEventDto("OrderCreated"),
            BuildEventDto("UserCreated"),
            BuildEventDto("PaymentReceived")
        };

        _factory.SubscriptionEventServiceMock
            .Setup(s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>
                .Success(events, "Subscribed events fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(events.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetSubscribedEvents_ForwardsSubscriptionIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId     = Guid.Empty;

        _factory.SubscriptionEventServiceMock
            .Setup(s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(subscriptionId));

        // Assert — correct route parameter forwarded to service
        Assert.Equal(subscriptionId, capturedId);
    }

    [Fact]
    public async Task GetSubscribedEvents_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedEvents_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        _factory.SubscriptionEventServiceMock.Verify(
            s => s.GetSubscribedEventsAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSubscribedEvents_NonGuidSubscriptionId_Returns404()
    {
        // Route constraint {subscriptionId:guid} causes ASP.NET Core
        // to return 404 when value does not match — no route found
        var response = await _client.GetAsync(
            "/api/WebhookSubscription/not-a-guid/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // SubscribeEvent — PUT api/WebhookSubscription/{subscriptionId}/events?eventName={name}
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task SubscribeEvent_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_ValidRequest_Returns200()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Successfully subscribed to event.", HttpStatusCode.OK));

        // Act
        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task SubscribeEvent_SubscriptionNotFound_Returns404()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription with Id does not exist.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task SubscribeEvent_EventNotFound_Returns400()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event does not exist.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=NonExistentEvent", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_AlreadySubscribed_Returns409Conflict()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event already exists for the subscription.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_ForwardsSubscriptionIdAndEventNameToService()
    {
        // Arrange
        var subscriptionId      = Guid.NewGuid();
        var capturedId          = Guid.Empty;
        var capturedEventName   = string.Empty;

        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((id, name, _) =>
            {
                capturedId        = id;
                capturedEventName = name;
            })
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscribed.", HttpStatusCode.OK));

        // Act
        await _client.PutAsync(
            $"{BaseRoute(subscriptionId)}?eventName=OrderCreated", null);

        // Assert
        Assert.Equal(subscriptionId,  capturedId);
        Assert.Equal("OrderCreated",  capturedEventName);
    }

    [Fact]
    public async Task SubscribeEvent_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscribed.", HttpStatusCode.OK));

        // Act
        await _client.PutAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated", null);

        // Assert
        _factory.SubscriptionEventServiceMock.Verify(
            s => s.SubscribeToEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // UnsubscribeEvent — DELETE api/WebhookSubscription/{subscriptionId}/events?eventName={name}
    // Requires: Any authenticated user
    // =========================================================================

    [Fact]
    public async Task UnsubscribeEvent_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeEvent_ValidRequest_Returns200()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Successfully unsubscribed from event.", HttpStatusCode.OK));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task UnsubscribeEvent_SubscriptionNotFound_Returns404()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription does not exist for event.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
    }

    [Fact]
    public async Task UnsubscribeEvent_EventNotFound_Returns400()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event does not exist.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=NonExistentEvent");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeEvent_ForwardsSubscriptionIdAndEventNameToService()
    {
        // Arrange
        var subscriptionId    = Guid.NewGuid();
        var capturedId        = Guid.Empty;
        var capturedEventName = string.Empty;

        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((id, name, _) =>
            {
                capturedId        = id;
                capturedEventName = name;
            })
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Unsubscribed.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync(
            $"{BaseRoute(subscriptionId)}?eventName=OrderCreated");

        // Assert
        Assert.Equal(subscriptionId, capturedId);
        Assert.Equal("OrderCreated", capturedEventName);
    }

    [Fact]
    public async Task UnsubscribeEvent_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeEvent_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.SubscriptionEventServiceMock
            .Setup(s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Unsubscribed.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?eventName=OrderCreated");

        // Assert
        _factory.SubscriptionEventServiceMock.Verify(
            s => s.UnsubscribeFromEventAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
/// <see cref="WebhookSubscriptionEventController"/> integration tests.
///
/// Follows the same pattern as <see cref="WebhookSubscriptionWebApiFactory"/>:
/// <list type="bullet">
///   <item><description><see cref="IWebhookSubscriptionEventService"/> replaced with a Moq mock.</description></item>
///   <item><description><see cref="ICacheService"/> replaced with a Moq mock seeded in
///   <see cref="ResetMocks"/> so <see cref="CustomAuthenticationFilter"/> passes
///   authenticated requests through without rejecting on default Guid.</description></item>
///   <item><description><see cref="SubscriptionEventTestAuthHandler"/> carries both USER and Admin
///   roles so all endpoints are reachable.</description></item>
/// </list>
/// </summary>
public sealed class SubscriptionEventWebApiFactory
    : WebApplicationFactory<Program>
{
    public Mock<IWebhookSubscriptionEventService> SubscriptionEventServiceMock { get; } = new();
    public Mock<Core.Interfaces.Helpers.ICacheService>                    CacheServiceMock             { get; } = new();

    /// <summary>
    /// Resets all mock setups and recorded invocations before each test method.
    /// Re-applies the cache setup after reset using the typed
    /// <see cref="SubscriptionEventTestAuthHandler.TestJtiGuid"/> so
    /// <see cref="CustomAuthenticationFilter"/> never sees a default Guid.
    /// </summary>
    public void ResetMocks()
    {
        SubscriptionEventServiceMock.Reset();
        CacheServiceMock.Reset();

        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                SubscriptionEventTestAuthHandler.TestEmail))
            .ReturnsAsync(SubscriptionEventTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebhookSubscriptionEventService>();
            services.RemoveAll<Core.Interfaces.Helpers.ICacheService>();

            services.AddSingleton(SubscriptionEventServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SubscriptionEventTestAuthHandler>(
                    SubscriptionEventTestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = SubscriptionEventTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme    = SubscriptionEventTestAuthHandler.SchemeName;
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
/// roles so all endpoints in <see cref="WebhookSubscriptionEventController"/> are
/// reachable in tests.
///
/// <see cref="TestEmail"/> and <see cref="TestJtiGuid"/> are seeded into the
/// cache mock in <see cref="SubscriptionEventWebApiFactory.ResetMocks"/> so
/// <see cref="CustomAuthenticationFilter"/> finds a non-default cached JTI
/// matching the token JTI claim and passes the request through.
///
/// Email and JTI are distinct from those in other test auth handlers to
/// prevent cache key collisions when multiple factories run in the same process.
/// </summary>
public sealed class SubscriptionEventTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SubscriptionEventTestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000004";
    public const string TestEmail  = "TESTSUBSCRIPTIONEVENT@ACME.COM";

    /// <summary>String form used in the JTI claim (requires string).</summary>
    public const string TestJti = "00000000-0000-0000-0000-000000000095";

    /// <summary>
    /// Typed Guid used in <see cref="SubscriptionEventWebApiFactory.ResetMocks"/>
    /// to avoid <see cref="Guid.Parse"/> and prevent a startup failure from a
    /// malformed constant.
    /// </summary>
    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000095");

    public SubscriptionEventTestAuthHandler(
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
