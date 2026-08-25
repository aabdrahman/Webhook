using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Org.BouncyCastle.Tls.Crypto;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Encodings.Web;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// A shared <see cref="WebApplicationFactory{TEntryPoint}"/> that replaces
/// all service dependencies with Moq mocks so controller tests run in-process
/// without any database, SMTP, or external service.
///
/// Each test class receives its own factory instance via
/// <see cref="IAsyncLifetime"/> so mocks are fresh per test class.
/// </summary>
public sealed class WebApiFactory : WebApplicationFactory<Program>
{
    public Mock<Core.Interfaces.Services.IAuthenticationService> AuthenticationServiceMock { get; } = new();
    public Mock<IUserService> UserServiceMock { get; } = new();
    public Mock<IOtpService> OtpServiceMock { get; } = new();
    public Mock<ICacheService> CacheServiceMock { get; } = new();

    // Stored as a field so ResetMocks can re-apply the no-op setup after clearing
    private Mock<CustomAuthenticationFilter>? _filterMock;

    public void ResetMocks()
    {
        AuthenticationServiceMock.Reset();
        UserServiceMock.Reset();
        OtpServiceMock.Reset();
        CacheServiceMock.Reset();

        // Re-apply no-op AFTER reset — this is the critical line
        // Without this the filter runs real logic after mocks are cleared
        _filterMock?
            .Setup(f => f.OnAuthorizationAsync(It.IsAny<AuthorizationFilterContext>()))
            .Returns(Task.CompletedTask);

        CacheServiceMock
                .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                    TestAuthHandler.TestEmail))
                .ReturnsAsync(TestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        Environment.SetEnvironmentVariable("webhook_secret_key", Random.Shared.GetHexString(16));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<Core.Interfaces.Services.IAuthenticationService>();
            services.RemoveAll<IUserService>();
            services.RemoveAll<IOtpService>();
            services.RemoveAll<ICacheService>();

            services.AddSingleton(AuthenticationServiceMock.Object);
            services.AddSingleton(UserServiceMock.Object);
            services.AddSingleton(OtpServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                    TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });

            // Remove filter from global pipeline regardless of how it was added
            services.PostConfigure<MvcOptions>(opts =>
            {
                var toRemove = opts.Filters.FirstOrDefault(f =>
                    f is ServiceFilterAttribute sfa &&
                    sfa.ServiceType == typeof(CustomAuthenticationFilter));

                if (toRemove is not null)
                    opts.Filters.Remove(toRemove);
            });

            // Build and store the filter mock so ResetMocks can re-apply setup
            _filterMock = new Mock<CustomAuthenticationFilter>(
                CacheServiceMock.Object,
                Mock.Of<ILogger<CustomAuthenticationFilter>>());

            _filterMock
                .Setup(f => f.OnAuthorizationAsync(It.IsAny<AuthorizationFilterContext>()))
                .Returns(Task.CompletedTask);

            services.RemoveAll<CustomAuthenticationFilter>();
            services.AddSingleton(_filterMock.Object);
        });
    }
}


/// <summary>
/// A test authentication handler that automatically authenticates every
/// incoming request as a known test user. Used in controller integration
/// tests to bypass JWT validation so tests focus on controller behaviour
/// rather than authentication mechanics.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000001";
    public const string TestEmail = "TEST@ACME.COM";
    public const string TestRole = "USER";

    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000097");

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId),
            new Claim(ClaimTypes.Email,          TestEmail),
            new Claim(ClaimTypes.Role,           TestRole),
            new Claim(ClaimTypes.Role,           "Admin"),
            new Claim(JwtRegisteredClaimNames.Jti, TestAuthHandler.TestJtiGuid.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>
/// A no-op authorization filter that replaces <see cref="CustomAuthenticationFilter"/>
/// in controller integration tests. Skips JTI cache validation entirely so tests
/// focus on controller and service behaviour rather than authentication mechanics.
///
/// Implements <see cref="IAsyncAuthorizationFilter"/> directly rather than
/// subclassing <see cref="CustomAuthenticationFilter"/> because
/// <see cref="CustomAuthenticationFilter.OnAuthorizationAsync"/> is not marked
/// virtual and cannot be overridden.
///
/// The real filter behaviour is covered by its own dedicated tests in
/// <see cref="CustomAuthenticationFilterTests"/> and
/// <see cref="CustomAuthenticationFilterIntegrationTests"/>.
/// </summary>
public sealed class PassThroughAuthFilter : CustomAuthenticationFilter
{
    public PassThroughAuthFilter(ICacheService cacheService, ILogger<CustomAuthenticationFilter> logger) : base(cacheService, logger)
    {
    }

    public override Task OnAuthorizationAsync(AuthorizationFilterContext context)
        => Task.CompletedTask;
}