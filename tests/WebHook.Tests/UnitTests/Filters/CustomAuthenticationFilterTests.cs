using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.UnitTests.Filters;

public sealed class CustomAuthenticationFilterTests
{
    // -------------------------------------------------------------------------
    // Fields & helpers
    // -------------------------------------------------------------------------

    private readonly Mock<ICacheService> _cacheServiceMock;
    private readonly Mock<ILogger<CustomAuthenticationFilter>> _loggerMock;
    private readonly CustomAuthenticationFilter _sut;

    private const string ValidEmail = "JOHN@ACME.COM"; // normalised (uppercase)
    private static readonly Guid ValidJti = Guid.NewGuid();
    private const string ValidBearer = "Bearer valid.jwt.token";

    public CustomAuthenticationFilterTests()
    {
        _cacheServiceMock = new Mock<ICacheService>();
        _loggerMock = new Mock<ILogger<CustomAuthenticationFilter>>();
        _sut = new CustomAuthenticationFilter(_cacheServiceMock.Object, _loggerMock.Object);
    }

    // ── Context builder ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds an AuthorizationFilterContext with configurable headers,
    /// claims principal, and optional [AllowAnonymous] metadata.
    /// </summary>
    private static AuthorizationFilterContext BuildContext(
        string? authorizationHeader = ValidBearer,
        ClaimsPrincipal? user = null,
        bool allowAnonymous = false)
    {
        var httpContext = new DefaultHttpContext();

        if (authorizationHeader is not null)
            httpContext.Request.Headers[HeaderNames.Authorization] = authorizationHeader;

        httpContext.User = user ?? BuildValidPrincipal();

        var actionDescriptor = new ActionDescriptor();

        if (allowAnonymous)
            actionDescriptor.EndpointMetadata = [new AllowAnonymousAttribute()];

        var routeData = new RouteData();

        return new AuthorizationFilterContext(
            new ActionContext(httpContext, routeData, actionDescriptor),
            []);
    }

    /// <summary>
    /// Builds a ClaimsPrincipal that contains a valid JTI and email claim.
    /// </summary>
    private static ClaimsPrincipal BuildValidPrincipal(
        string? jti = null,
        string? email = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti   ?? ValidJti.ToString()),
            new(ClaimTypes.Email,            email ?? ValidEmail)
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    /// <summary>
    /// Configures the cache mock to return a specific Guid for the given email key.
    /// </summary>
    private void SetupCache(string email, Guid jti)
        => _cacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(email))
            .ReturnsAsync(jti);

    private void SetupCacheMiss(string email)
        => SetupCache(email, default);

    private static void AssertUnauthorized(AuthorizationFilterContext context)
    {
        Assert.IsType<UnauthorizedObjectResult>(context.Result);
        var result = (UnauthorizedObjectResult)context.Result;
        var body = Assert.IsType<GenericResponse<string>>(result.Value);
        Assert.False(body.IsSuccessful);
        Assert.Equal("Unauthorized Access.", body.ResponseMessage);
    }

    private static void AssertPassed(AuthorizationFilterContext context)
        => Assert.Null(context.Result);

    // =========================================================================
    // [AllowAnonymous] bypass
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_AllowAnonymousEndpoint_SkipsValidation()
    {
        // Arrange — context with [AllowAnonymous] and NO auth header
        var context = BuildContext(authorizationHeader: null, allowAnonymous: true);

        // Act
        await _sut.OnAuthorizationAsync(context);

        // Assert — filter did not set a result, passed straight through
        AssertPassed(context);
        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(It.IsAny<string>()), Times.Never);
    }

    // =========================================================================
    // Authorization header validation
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_MissingAuthorizationHeader_Returns401()
    {
        var context = BuildContext(authorizationHeader: null);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_EmptyAuthorizationHeader_Returns401()
    {
        var context = BuildContext(authorizationHeader: "");

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhitespaceAuthorizationHeader_Returns401()
    {
        var context = BuildContext(authorizationHeader: "   ");

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Theory]
    [InlineData("Basic dXNlcjpwYXNz")]          // Basic auth
    [InlineData("bearer valid.jwt.token")]       // lowercase bearer
    [InlineData("BEARERvalid.jwt.token")]        // no space
    [InlineData("valid.jwt.token")]              // no scheme
    public async Task OnAuthorizationAsync_NonBearerScheme_Returns401(string header)
    {
        var context = BuildContext(authorizationHeader: header);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_BearerWithNoToken_Returns401()
    {
        // "Bearer " with nothing after the space
        var context = BuildContext(authorizationHeader: "Bearer ");

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_BearerWithOnlyWhitespace_Returns401()
    {
        var context = BuildContext(authorizationHeader: "Bearer    ");

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    // =========================================================================
    // Claims validation
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_MissingJtiClaim_Returns401()
    {
        // Principal with email but no JTI
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, ValidEmail)], "TestAuth"));

        var context = BuildContext(user: principal);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_JtiClaimNotValidGuid_Returns401()
    {
        var principal = BuildValidPrincipal(jti: "not-a-guid");
        var context = BuildContext(user: principal);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_MissingEmailClaim_Returns401()
    {
        // Principal with JTI but no email
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Jti, ValidJti.ToString())], "TestAuth"));

        var context = BuildContext(user: principal);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_EmptyEmailClaim_Returns401()
    {
        var principal = BuildValidPrincipal(email: "");
        var context = BuildContext(user: principal);

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    // =========================================================================
    // Cache validation
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_CacheMiss_Returns401()
    {
        // Cache returns default(Guid) — no session found
        SetupCacheMiss(ValidEmail);
        var context = BuildContext();

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(ValidEmail), Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_CachedJtiDoesNotMatchTokenJti_Returns401()
    {
        // Cache has a different JTI — token has been superseded by a refresh
        SetupCache(ValidEmail, Guid.NewGuid()); // different Guid from ValidJti
        var context = BuildContext();

        await _sut.OnAuthorizationAsync(context);

        AssertUnauthorized(context);
    }

    // =========================================================================
    // Happy path
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_ValidTokenAndMatchingCachedJti_Passes()
    {
        // Arrange — cache returns the same JTI as the token claims
        SetupCache(ValidEmail, ValidJti);
        var context = BuildContext();

        // Act
        await _sut.OnAuthorizationAsync(context);

        // Assert — no result set means the filter passed the request through
        AssertPassed(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_ValidRequest_OnlyCacheCalledOnce()
    {
        SetupCache(ValidEmail, ValidJti);
        var context = BuildContext();

        await _sut.OnAuthorizationAsync(context);

        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(ValidEmail), Times.Once);
    }

    [Fact]
    public async Task OnAuthorizationAsync_ValidRequest_CacheNotCalledForOtherKeys()
    {
        SetupCache(ValidEmail, ValidJti);
        var context = BuildContext();

        await _sut.OnAuthorizationAsync(context);

        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(It.Is<string>(k => k != ValidEmail)),
            Times.Never);
    }

    // =========================================================================
    // Cache not called for early exits
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_MissingHeader_CacheNeverCalled()
    {
        var context = BuildContext(authorizationHeader: null);

        await _sut.OnAuthorizationAsync(context);

        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_InvalidClaims_CacheNeverCalled()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no claims
        var context = BuildContext(user: principal);

        await _sut.OnAuthorizationAsync(context);

        _cacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(It.IsAny<string>()), Times.Never);
    }

    // =========================================================================
    // Cancellation
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_CacheThrows_DoesNotSwallowException()
    {
        _cacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("Cache unavailable."));

        var context = BuildContext();

        // Filter should not swallow infrastructure exceptions — let them bubble
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.OnAuthorizationAsync(context));
    }
}