using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
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
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Interfaces.Services;
using WebHook.Tests.Utilities;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebHook.Api.Controllers.WebhookSubscriptionVerificationController"/>.
///
/// TESTING STRATEGY:
/// <see cref="ISubscriptionVerificationService"/> and the cache service are replaced with Moq mocks via
/// <see cref="SubscriptionVerificationWebApiFactory"/>, so the tests cover routing, authentication, role
/// authorisation, rate-limit metadata, parameter forwarding, status code mapping and exception handling.
/// The handshake itself is covered by the service, processor and worker tests.
///
/// The test auth handler takes its roles from the <c>X-Test-Roles</c> request header (default <c>USER</c>) so each
/// test can act as a different kind of caller.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET api/WebhookSubscription/{id}/verifications         — GetSubscriptionVerifications  [Authorize]</description></item>
///   <item><description>PUT api/WebhookSubscription/{id}/verifications/verify  — VerifySubscription            [Authorize(Roles = "User,USER")]</description></item>
/// </list>
/// </summary>
public sealed class WebhookSubscriptionVerificationControllerIntegrationTests
    : IClassFixture<SubscriptionVerificationWebApiFactory>, IAsyncLifetime
{
    private readonly SubscriptionVerificationWebApiFactory _factory;
    private HttpClient _client = null!;

    public WebhookSubscriptionVerificationControllerIntegrationTests(SubscriptionVerificationWebApiFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocks();

        // The custom authentication filter requires a Bearer header; the value is irrelevant because the
        // test auth handler has already populated the user.
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ helpers

    private static string ListRoute(Guid subscriptionId) => $"/api/WebhookSubscription/{subscriptionId}/verifications";
    private static string VerifyRoute(Guid subscriptionId) => $"{ListRoute(subscriptionId)}/verify";

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? roles = null)
    {
        var request = new HttpRequestMessage(method, route);
        if (roles is not null)
            request.Headers.Add(SubscriptionVerificationTestAuthHandler.RolesHeader, roles);

        return _client.SendAsync(request);
    }

    private void SetupVerify(GenericResponse<string> response)
        => _factory.VerificationServiceMock
            .Setup(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private void SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>> response)
        => _factory.VerificationServiceMock
            .Setup(s => s.GetSubscriptionVerifications(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    private static SubscriptionVerificationDto BuildDto(string status = "Success", DateTimeOffset? attemptedAt = null)
        => new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5), attemptedAt ?? DateTimeOffset.UtcNow.AddMinutes(-4), status);

    // =========================================================================
    // GET api/WebhookSubscription/{id}/verifications
    // =========================================================================

    [Fact]
    public async Task GetVerifications_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.GetSubscriptionVerifications(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetVerifications_RecordsExist_Returns200WithRecords()
    {
        var records = new List<SubscriptionVerificationDto> { BuildDto("Success"), BuildDto("Failed"), BuildDto("Pending") };
        SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Success(records, "Subscription verification records fetched successfully.", HttpStatusCode.OK));

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.Equal(new[] { "Success", "Failed", "Pending" }, body.ResponseData!.Select(x => x.Status));
        Assert.Equal(records.Select(x => x.Id), body.ResponseData!.Select(x => x.Id));
    }

    [Fact]
    public async Task GetVerifications_ResponseNeverExposesTheChallengeToken()
    {
        SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Success([BuildDto()], "ok", HttpStatusCode.OK));

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("challenge", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("responseDetails", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVerifications_SubscriptionNotFoundForCaller_Returns404()
    {
        SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Failure(null, "Subscription not found.", HttpStatusCode.NotFound));

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>>();
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetVerifications_ServiceReturns500_Returns500()
    {
        SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Failure(null, "An error occurred processing request.", HttpStatusCode.InternalServerError));

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetVerifications_ServiceThrows_Returns500WithGenericEnvelope()
    {
        _factory.VerificationServiceMock
            .Setup(s => s.GetSubscriptionVerifications(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database exploded"));

        var response = await _client.GetAsync(ListRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.False(body!.IsSuccessful);
        Assert.Equal("An error occurred invoking endpoint.", body.ResponseMessage);
    }

    [Fact]
    public async Task GetVerifications_ForwardsSubscriptionIdToService()
    {
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;
        _factory.VerificationServiceMock
            .Setup(s => s.GetSubscriptionVerifications(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Success([], "ok", HttpStatusCode.OK));

        await _client.GetAsync(ListRoute(subscriptionId));

        Assert.Equal(subscriptionId, capturedId);
    }

    [Theory]
    [InlineData("USER")]
    [InlineData("User")]
    [InlineData("Admin")]
    public async Task GetVerifications_AnyAuthenticatedRole_IsAllowed(string role)
    {
        SetupList(GenericResponse<IReadOnlyList<SubscriptionVerificationDto>>.Success([], "ok", HttpStatusCode.OK));

        var response = await SendAsync(HttpMethod.Get, ListRoute(Guid.NewGuid()), roles: role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetVerifications_RouteIdNotAGuid_Returns404WithoutCallingService()
    {
        var response = await _client.GetAsync("/api/WebhookSubscription/not-a-guid/verifications");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.GetSubscriptionVerifications(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // PUT api/WebhookSubscription/{id}/verifications/verify
    // =========================================================================

    [Fact]
    public async Task Verify_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsync(VerifyRoute(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_AdminOnly_Returns403AndDoesNotCallService()
    {
        var response = await SendAsync(HttpMethod.Put, VerifyRoute(Guid.NewGuid()), roles: "Admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("USER")]
    [InlineData("User")]
    public async Task Verify_UserRoleInEitherCasing_IsAllowed(string role)
    {
        SetupVerify(GenericResponse<string>.Success("Operation Successful.", "Subscription callback URL verification initiated successfully.", HttpStatusCode.OK));

        var response = await SendAsync(HttpMethod.Put, VerifyRoute(Guid.NewGuid()), roles: role);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Verify_Success_Returns200WithEnvelope()
    {
        SetupVerify(GenericResponse<string>.Success("Operation Successful.", "Subscription callback URL verification initiated successfully.", HttpStatusCode.OK));

        var response = await _client.PutAsync(VerifyRoute(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.True(body!.IsSuccessful);
        Assert.Equal("Operation Successful.", body.ResponseData);
    }

    [Fact]
    public async Task Verify_ForwardsSubscriptionIdAndCallsServiceOnce()
    {
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;
        _factory.VerificationServiceMock
            .Setup(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>.Success("Operation Successful.", "ok", HttpStatusCode.OK));

        await _client.PutAsync(VerifyRoute(subscriptionId), content: null);

        Assert.Equal(subscriptionId, capturedId);
        _factory.VerificationServiceMock.Verify(s => s.VerifySubscriptionCallbackUrl(subscriptionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Subscription not found.")]
    [InlineData(HttpStatusCode.Conflict, "A verification for this subscription is already in progress.")]
    [InlineData(HttpStatusCode.Conflict, "Subscription is not in inactive status.")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "The verification could not be started. Please try again later.")]
    [InlineData(HttpStatusCode.BadRequest, "An error occurred processing request.")]
    [InlineData(HttpStatusCode.InternalServerError, "An error occurred processing request.")]
    public async Task Verify_ServiceFailure_MapsStatusCodeAndMessage(HttpStatusCode status, string message)
    {
        SetupVerify(GenericResponse<string>.Failure("Operation Failed.", message, status));

        var response = await _client.PutAsync(VerifyRoute(Guid.NewGuid()), content: null);

        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.False(body!.IsSuccessful);
        Assert.Equal(message, body.ResponseMessage);
    }

    [Fact]
    public async Task Verify_ServiceThrows_Returns500WithGenericEnvelope()
    {
        _factory.VerificationServiceMock
            .Setup(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("queue exploded"));

        var response = await _client.PutAsync(VerifyRoute(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.False(body!.IsSuccessful);
        Assert.Equal("An error occurred invoking endpoint.", body.ResponseMessage);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Verify_WrongHttpMethod_Returns405(string method)
    {
        var response = await SendAsync(new HttpMethod(method), VerifyRoute(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Verify_RouteIdNotAGuid_Returns404WithoutCallingService()
    {
        var response = await _client.PutAsync("/api/WebhookSubscription/not-a-guid/verifications/verify", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        _factory.VerificationServiceMock.Verify(s => s.VerifySubscriptionCallbackUrl(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // Cross-cutting
    // =========================================================================

    [Fact]
    public void BothEndpoints_HaveThePerUserRateLimitPolicyApplied()
    {
        // The factory replaces the real limiter with a no-op policy, so assert on the endpoint metadata instead.
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.Contains("verifications", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Equal(2, endpoints.Count);
        foreach (var endpoint in endpoints)
            Assert.Equal("per-user-rating", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to the subscription verification controller tests.
/// <see cref="ISubscriptionVerificationService"/> and the cache service are mocks; the cache mock is seeded in
/// <see cref="ResetMocks"/> so the real authentication filter finds a cached JTI matching the test token.
/// </summary>
public sealed class SubscriptionVerificationWebApiFactory : WebApplicationFactory<Program>
{
    public Mock<ISubscriptionVerificationService> VerificationServiceMock { get; } = new();
    public Mock<Core.Interfaces.Helpers.ICacheService> CacheServiceMock { get; } = new();

    public void ResetMocks()
    {
        VerificationServiceMock.Reset();
        CacheServiceMock.Reset();

        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(SubscriptionVerificationTestAuthHandler.TestEmail))
            .ReturnsAsync(SubscriptionVerificationTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTemporaryCacheDatabase();
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISubscriptionVerificationService>();
            services.RemoveAll<Core.Interfaces.Helpers.ICacheService>();

            services.AddSingleton(VerificationServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SubscriptionVerificationTestAuthHandler>(
                    SubscriptionVerificationTestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = SubscriptionVerificationTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = SubscriptionVerificationTestAuthHandler.SchemeName;
            });

            services.RemoveAll<IConfigureOptions<RateLimiterOptions>>();
            services.RemoveAll<IOptionsChangeTokenSource<RateLimiterOptions>>();

            services.AddRateLimiter(opts =>
            {
                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                opts.AddPolicy("request-otp-limit", context => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("validate-otp-limit", context => RateLimitPartition.GetNoLimiter("test"));
                opts.AddPolicy("per-user-rating", context => RateLimitPartition.GetNoLimiter("test"));
            });
        });
    }
}

// =============================================================================
// Test auth handler
// =============================================================================

/// <summary>
/// Authenticates every request as a test user. The roles come from the <see cref="RolesHeader"/> request header
/// (comma separated) and default to <c>USER</c>, so tests can act as a user, an admin, or a differently-cased role.
/// </summary>
public sealed class SubscriptionVerificationTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SubscriptionVerificationTestAuth";
    public const string RolesHeader = "X-Test-Roles";
    public const string TestUserId = "00000000-0000-0000-0000-000000000008";
    public const string TestEmail = "TESTSUBSCRIPTIONVERIFICATION@ACME.COM";
    public const string TestJti = "00000000-0000-0000-0000-000000000099";

    public static readonly Guid TestJtiGuid = new(TestJti);

    public SubscriptionVerificationTestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, TestUserId),
            new(ClaimTypes.Email, TestEmail),
            new(JwtRegisteredClaimNames.Jti, TestJti)
        };

        string roles = Request.Headers.TryGetValue(RolesHeader, out var header) ? header.ToString() : "USER";
        claims.AddRange(roles.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                             .Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
