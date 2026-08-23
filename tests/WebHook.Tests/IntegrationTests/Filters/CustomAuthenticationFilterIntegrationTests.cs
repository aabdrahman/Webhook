using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.Interfaces.Helpers;

namespace WebHook.IntegrationTests.Filters;

/// <summary>
/// Integration tests for <see cref="CustomAuthenticationFilter"/> proving
/// correct behaviour through the real ASP.NET Core HTTP pipeline.
///
/// Uses a minimal WebApplicationFactory with a test controller that
/// has the filter applied. JWT middleware is registered with a known
/// test secret so tokens can be generated and validated in-process.
/// </summary>
public sealed class CustomAuthenticationFilterIntegrationTests: IClassFixture<FilterWebApplicationFactory>, IAsyncLifetime
{
    private readonly FilterWebApplicationFactory _factory;
    private HttpClient _client = null!;

    private const string TestSecret = "integration-test-secret-key-32chars!!";
    private const string ValidEmail = "JOHN@ACME.COM";
    private static readonly Guid ValidJti = Guid.NewGuid();

    public CustomAuthenticationFilterIntegrationTests(FilterWebApplicationFactory factory)
        => _factory = factory;

    public Task InitializeAsync()
    {
        // Create a fresh client per test — do NOT dispose the factory here
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Dispose only the client — the factory is disposed by xUnit
        // when the IClassFixture lifetime ends (after ALL tests in the class)
        _client.Dispose();
        await Task.CompletedTask;
    }

    // ── Token builder ─────────────────────────────────────────────────────────

    private static string BuildToken(
        Guid? jti = null,
        string? email = null,
        bool expired = false)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = expired
            ? DateTime.UtcNow.AddHours(-1)
            : DateTime.UtcNow.AddHours(1);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, (jti ?? ValidJti).ToString()),
            new(ClaimTypes.Email,            email ?? ValidEmail)
        };

        var token = new JwtSecurityToken(
            issuer: "webhook-service",
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // =========================================================================
    // Happy path
    // =========================================================================

    [Fact]
    public async Task ProtectedEndpoint_ValidTokenWithMatchingCachedJti_Returns200()
    {
        // Arrange — cache mock returns JTI matching the token
        _factory.CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(ValidEmail))
            .ReturnsAsync(ValidJti);

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken());

        // Act
        var response = await _client.GetAsync("/test/protected");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Missing / malformed header
    // =========================================================================

    [Fact]
    public async Task ProtectedEndpoint_NoAuthorizationHeader_Returns401()
    {
        var response = await _client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_BasicAuthHeader_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

        var response = await _client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Cache failures
    // =========================================================================

    [Fact]
    public async Task ProtectedEndpoint_CacheMiss_Returns401()
    {
        _factory.CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(ValidEmail))
            .ReturnsAsync(default(Guid));

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken());

        var response = await _client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_JtiMismatch_Returns401()
    {
        // Cache has a different JTI — simulates a token superseded by refresh
        _factory.CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(ValidEmail))
            .ReturnsAsync(Guid.NewGuid()); // different from ValidJti in token

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", BuildToken());

        var response = await _client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // [AllowAnonymous] bypass
    // =========================================================================

    [Fact]
    public async Task AnonymousEndpoint_NoToken_Returns200()
    {
        // Cache should never be called for anonymous endpoints
        var response = await _client.GetAsync("/test/anonymous");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _factory.CacheServiceMock.Verify(
            c => c.GetItemsFromCacheAsync<Guid>(It.IsAny<string>()), Times.Never);
    }
}

/// <summary>
/// A minimal WebApplicationFactory that registers a test controller
/// with <see cref="CustomAuthenticationFilter"/> applied, and replaces
/// <see cref="ICacheService"/> with a Moq mock for test control.
/// </summary>
public sealed class FilterWebApplicationFactory
    : WebApplicationFactory<Program>
{
    public Mock<ICacheService> CacheServiceMock { get; } = new();

    private const string TestSecret = "integration-test-secret-key-32chars!!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((context, config) =>
        {
            // Override the JWT secret used by your AuthenticationService
            // so token generation in tests matches validation in the pipeline
            Environment.SetEnvironmentVariable("webhook_secret_key", TestSecret);
        });

        builder.ConfigureServices(services =>
        {
            // Replace real cache with mock
            services.RemoveAll<ICacheService>();
            services.AddSingleton(CacheServiceMock.Object);

            // Register the filter
            services.AddScoped<CustomAuthenticationFilter>();

            // Register test controller
            services.AddControllers()
                .AddApplicationPart(typeof(FilterTestController).Assembly);

            // Override JWT validation parameters on the EXISTING Bearer scheme
            // rather than registering a new one — avoids "Scheme already exists"
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, opts =>
                {
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = "webhook-service",
                        ValidateAudience = false,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(TestSecret))
                    };
                });
        });
    }
}

//public sealed class FilterWebApplicationFactory
//    : WebApplicationFactory<Program>
//{
//    public Mock<ICacheService> CacheServiceMock { get; } = new();

//    private const string TestSecret = "integration-test-secret-key-32chars!!";

//    protected override void ConfigureWebHost(IWebHostBuilder builder)
//    {
//        builder.UseEnvironment("Testing");

//        builder.ConfigureServices(services =>
//        {
//            // Replace real cache with mock
//            services.RemoveAll<ICacheService>();
//            services.AddSingleton(CacheServiceMock.Object);

//            // Register the filter and test controller
//            services.AddScoped<CustomAuthenticationFilter>();

//            services.AddControllers()
//                .AddApplicationPart(typeof(FilterTestController).Assembly);

//            // JWT authentication with test secret — must match token builder
//            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//                .AddJwtBearer(opts =>
//                {
//                    opts.TokenValidationParameters = new TokenValidationParameters
//                    {
//                        ValidateIssuer = true,
//                        ValidIssuer = "webhook-service",
//                        ValidateAudience = false,
//                        ValidateLifetime = true,
//                        ValidateIssuerSigningKey = true,
//                        IssuerSigningKey = new SymmetricSecurityKey(
//                            Encoding.UTF8.GetBytes(TestSecret))
//                    };
//                });

//            services.AddAuthorization();
//        });

//        builder.Configure(app =>
//        {
//            app.UseRouting();
//            app.UseAuthentication();
//            app.UseAuthorization();
//            app.UseEndpoints(e => e.MapControllers());
//        });
//    }
//}

/// <summary>
/// Minimal test controller used only by integration tests.
/// Provides one protected endpoint (filter applied) and one anonymous endpoint.
/// </summary>
[ApiController]
[Route("test")]
public sealed class FilterTestController : ControllerBase
{
    [HttpGet("protected")]
    [Authorize]
    [ServiceFilter(typeof(CustomAuthenticationFilter))]
    public IActionResult Protected() => Ok(new { ok = true });

    [HttpGet("anonymous")]
    [AllowAnonymous]
    [ServiceFilter(typeof(CustomAuthenticationFilter))]
    public IActionResult Anonymous() => Ok(new { ok = true });
}