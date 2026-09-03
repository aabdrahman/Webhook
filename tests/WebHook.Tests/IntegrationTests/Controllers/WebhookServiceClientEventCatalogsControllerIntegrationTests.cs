using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers.ServiceClients;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookServiceClientEventCatalogsController"/>.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET    /api/WebhookServiceClients/{id}/eventcatalogs                    — GetSubscribedCatalogs  [Admin]</description></item>
///   <item><description>POST   /api/WebhookServiceClients/{id}/eventcatalogs?catalogName={name} — SubscribeCatalog        [Admin]</description></item>
///   <item><description>DELETE /api/WebhookServiceClients/{id}/eventcatalogs?catalogName={name} — UnsubscribeCatalog      [Admin]</description></item>
/// </list>
/// </summary>
public sealed class WebhookServiceClientEventCatalogsControllerIntegrationTests
    : IClassFixture<WebApiFactory>, IAsyncLifetime
{
    private readonly WebApiFactory _factory;
    private HttpClient _client = null!;

    // Base route helper
    private static string BaseRoute(Guid serviceClientId)
        => $"/api/WebhookServiceClients/{serviceClientId}/eventcatalogs";

    public WebhookServiceClientEventCatalogsControllerIntegrationTests(
        WebApiFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "test-token");

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

    private static WebhookServiceClientCatalogDto BuildCatalogDto(
        string catalogName = "OrderCreated",
        bool   isActive    = true) => new()
        {
            Id          = Guid.NewGuid(),
            CatalogName = catalogName,
            IsActive    = isActive,
            ServiceClientId =Guid.NewGuid()
        };

    // =========================================================================
    // GetSubscribedCatalogs — GET /api/WebhookServiceClients/{id}/eventcatalogs
    // =========================================================================

    [Fact]
    public async Task GetSubscribedCatalogs_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_NoCatalogsFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>
                .Failure(null, "No catalog subscriptions found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_CatalogsExist_Returns200()
    {
        // Arrange
        var catalogs = new List<WebhookServiceClientCatalogDto>
        {
            BuildCatalogDto("OrderCreated"),
            BuildCatalogDto("OrderCancelled")
        };

        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>
                .Success(catalogs, "Catalogs fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(catalogs.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_ForwardsServiceClientIdToService()
    {
        // Arrange
        var serviceClientId  = Guid.NewGuid();
        var capturedId       = Guid.Empty;

        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, bool, CancellationToken>((id, _, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(serviceClientId));

        // Assert
        Assert.Equal(serviceClientId, capturedId);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_ForwardsIncludeDeactivatedParam()
    {
        // Arrange
        bool? capturedFlag = null;

        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, bool, CancellationToken>((_, flag, _) => capturedFlag = flag)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync($"{BaseRoute(Guid.NewGuid())}?includeDeactivated=true");

        // Assert
        Assert.True(capturedFlag);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_NonGuidServiceClientId_Returns404()
    {
        // Route constraint {serviceclientid:guid} returns 404 for non-GUID values
        var response = await _client.GetAsync(
            "/api/WebhookServiceClients/not-a-guid/eventcatalogs");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedCatalogs_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientCatalogDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(BaseRoute(Guid.NewGuid()));

        // Assert
        _factory.ServiceClientCatalogServiceMock.Verify(
            s => s.GetSubscribedCatalogsAsync(
                It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // SubscribeCatalog — POST /api/WebhookServiceClients/{id}/eventcatalogs?catalogName={name}
    // =========================================================================

    [Fact]
    public async Task SubscribeCatalog_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeCatalog_ValidRequest_Returns200()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.SubscribeToCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Event catalog subscribed successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PostAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task SubscribeCatalog_AlreadySubscribed_Returns409Conflict()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.SubscribeToCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Client is already subscribed to this catalog.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeCatalog_CatalogNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.SubscribeToCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Event catalog not found or is deactivated.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PostAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=NonExistentEvent", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubscribeCatalog_ForwardsServiceClientIdAndCatalogNameToService()
    {
        // Arrange
        var serviceClientId    = Guid.NewGuid();
        var capturedId         = Guid.Empty;
        var capturedCatalogName = string.Empty;

        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.SubscribeToCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((id, name, _) =>
            {
                capturedId          = id;
                capturedCatalogName = name;
            })
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscribed.", HttpStatusCode.OK));

        // Act
        await _client.PostAsync(
            $"{BaseRoute(serviceClientId)}?catalogName=OrderCreated", null);

        // Assert
        Assert.Equal(serviceClientId, capturedId);
        Assert.Equal("OrderCreated",  capturedCatalogName);
    }

    [Fact]
    public async Task SubscribeCatalog_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.SubscribeToCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // UnsubscribeCatalog — DELETE /api/WebhookServiceClients/{id}/eventcatalogs?catalogName={name}
    // =========================================================================

    [Fact]
    public async Task UnsubscribeCatalog_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeCatalog_ValidRequest_Returns200()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Event catalog unsubscribed successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task UnsubscribeCatalog_SubscriptionNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription not found or is already inactive.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=NonExistentEvent");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeCatalog_ForwardsServiceClientIdAndCatalogNameToService()
    {
        // Arrange
        var serviceClientId     = Guid.NewGuid();
        var capturedId          = Guid.Empty;
        var capturedCatalogName = string.Empty;

        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, CancellationToken>((id, name, _) =>
            {
                capturedId          = id;
                capturedCatalogName = name;
            })
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Unsubscribed.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync(
            $"{BaseRoute(serviceClientId)}?catalogName=OrderCreated");

        // Assert
        Assert.Equal(serviceClientId, capturedId);
        Assert.Equal("OrderCreated",  capturedCatalogName);
    }

    [Fact]
    public async Task UnsubscribeCatalog_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task UnsubscribeCatalog_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.ServiceClientCatalogServiceMock
            .Setup(s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Unsubscribed.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync(
            $"{BaseRoute(Guid.NewGuid())}?catalogName=OrderCreated");

        // Assert
        _factory.ServiceClientCatalogServiceMock.Verify(
            s => s.UnSubscribeFromCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

//// =============================================================================
//// Factory — shared by both controller test classes
//// =============================================================================

///// <summary>
///// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
///// <see cref="WebhookServiceClientsController"/> and
///// <see cref="WebhookServiceClientEventCatalogsController"/> integration tests.
/////
///// Follows the same pattern as <see cref="Controllers.WebApiFactory"/> — replaces all
///// relevant services with Moq mocks and registers the test auth handler and
///// no-op rate limiter policies.
///// </summary>
//public sealed class WebApiFactory : WebApplicationFactory<Program>
//{
//    public Mock<IWebhookServiceClientService>        ServiceClientServiceMock        { get; } = new();
//    public Mock<IWebhookServiceClientCatalogService> ServiceClientCatalogServiceMock { get; } = new();
//    public Mock<ICacheService>                       CacheServiceMock                { get; } = new();


//    public void ResetMocks()
//    {
//        ServiceClientServiceMock.Reset();
//        ServiceClientCatalogServiceMock.Reset();
//        CacheServiceMock.Reset();

//        // Re-seed cache after reset so CustomAuthenticationFilter passes
//        CacheServiceMock
//            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
//                ServiceClientsTestAuthHandler.TestEmail))
//            .ReturnsAsync(ServiceClientsTestAuthHandler.TestJtiGuid);
//    }

//    protected override void ConfigureWebHost(IWebHostBuilder builder)
//    {
//        builder.UseEnvironment("Testing");
//        Environment.SetEnvironmentVariable("webhook_secret_key", Random.Shared.GetHexString(16));

//        builder.ConfigureServices(services =>
//        {
//            services.RemoveAll<IWebhookServiceClientService>();
//            services.RemoveAll<IWebhookServiceClientCatalogService>();
//            services.RemoveAll<ICacheService>();

//            services.AddSingleton(ServiceClientServiceMock.Object);
//            services.AddSingleton(ServiceClientCatalogServiceMock.Object);
//            services.AddSingleton(CacheServiceMock.Object);

//            services.AddAuthentication()
//                .AddScheme<AuthenticationSchemeOptions, ServiceClientsTestAuthHandler>(
//                    ServiceClientsTestAuthHandler.SchemeName, _ => { });

//            services.PostConfigure<AuthenticationOptions>(opts =>
//            {
//                opts.DefaultAuthenticateScheme = ServiceClientsTestAuthHandler.SchemeName;
//                opts.DefaultChallengeScheme    = ServiceClientsTestAuthHandler.SchemeName;
//            });

//            // Remove CustomAuthenticationFilter from the global pipeline
//            services.PostConfigure<MvcOptions>(opts =>
//            {
//                var toRemove = opts.Filters.FirstOrDefault(f =>
//                    f is ServiceFilterAttribute sfa &&
//                    sfa.ServiceType == typeof(CustomAuthenticationFilter));

//                if (toRemove is not null)
//                    opts.Filters.Remove(toRemove);
//            });

//            // Replace real rate limiter policies with no-ops
//            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
//            services.RemoveAll<IOptionsChangeTokenSource<RateLimiterOptions>>();

//            services.AddRateLimiter(opts =>
//            {
//                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

//                opts.AddPolicy("request-otp-limit",  _ => RateLimitPartition.GetNoLimiter("test"));
//                opts.AddPolicy("validate-otp-limit", _ => RateLimitPartition.GetNoLimiter("test"));
//                opts.AddPolicy("per-user-rating",    _ => RateLimitPartition.GetNoLimiter("test"));
//            });

//            // Register the filter mock as a no-op
//            var filterMock = new Mock<CustomAuthenticationFilter>(
//                CacheServiceMock.Object,
//                Mock.Of<ILogger<CustomAuthenticationFilter>>());

//            filterMock
//                .Setup(f => f.OnAuthorizationAsync(It.IsAny<AuthorizationFilterContext>()))
//                .Returns(Task.CompletedTask);

//            services.RemoveAll<CustomAuthenticationFilter>();
//            services.AddSingleton(filterMock.Object);
//        });
//    }
//}

//// =============================================================================
//// Test auth handler
//// =============================================================================

///// <summary>
///// Auto-authenticates every request as a test user carrying both USER and Admin
///// roles so all endpoints in both service client controllers are reachable.
///// JTI and email are distinct from <see cref="TestAuthHandler"/> to prevent
///// cache key collisions when both factories run in the same process.
///// </summary>
//public sealed class ServiceClientsTestAuthHandler
//    : AuthenticationHandler<AuthenticationSchemeOptions>
//{
//    public const string SchemeName = "ServiceClientsTestAuth";
//    public const string TestUserId = "00000000-0000-0000-0000-000000000007";
//    public const string TestEmail  = "TESTSERVICECLIENTS@ACME.COM";
//    public const string TestJti    = "00000000-0000-0000-0000-000000000092";
//    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000092");

//    public ServiceClientsTestAuthHandler(
//        IOptionsMonitor<AuthenticationSchemeOptions> options,
//        ILoggerFactory logger,
//        UrlEncoder encoder) : base(options, logger, encoder) { }

//    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
//    {
//        var claims = new[]
//        {
//            new Claim(ClaimTypes.NameIdentifier, TestUserId),
//            new Claim(ClaimTypes.Email,          TestEmail),
//            new Claim(ClaimTypes.Role,           "USER"),
//            new Claim(ClaimTypes.Role,           "Admin"),
//            new Claim(JwtRegisteredClaimNames.Jti, TestJti)
//        };

//        var identity  = new ClaimsIdentity(claims, SchemeName);
//        var principal = new ClaimsPrincipal(identity);
//        var ticket    = new AuthenticationTicket(principal, SchemeName);

//        return Task.FromResult(AuthenticateResult.Success(ticket));
//    }
//}
