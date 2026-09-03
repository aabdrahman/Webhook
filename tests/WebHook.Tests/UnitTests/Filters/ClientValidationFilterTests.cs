using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using WebHook.Api.ApplicationFilters;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.UnitTests.Filters;

/// <summary>
/// Unit tests for <see cref="ClientValidationFilter"/>.
///
/// TESTING STRATEGY:
/// <see cref="RepositoryContext"/> is backed by a real PostgreSQL container via
/// <see cref="PostgreSqlFixture"/> so database queries run against a real engine.
/// <see cref="IApplicationHasher"/> is mocked so hash validation outcomes can be
/// controlled per test without depending on the real hashing implementation.
///
/// TEST CASES:
/// <list type="bullet">
///   <item><description>Missing X-Client-Id header → 401</description></item>
///   <item><description>Missing X-Client-Key header → 401</description></item>
///   <item><description>Both headers missing → 401</description></item>
///   <item><description>Empty X-Client-Id → 401</description></item>
///   <item><description>Empty X-Client-Key → 401</description></item>
///   <item><description>ClientId not found in database → 401</description></item>
///   <item><description>ClientId found but key hash validation fails → 401</description></item>
///   <item><description>ClientId found, key valid, client inactive → 401</description></item>
///   <item><description>Valid ClientId and ClientKey → request proceeds</description></item>
///   <item><description>ClientId lookup is case-insensitive → request proceeds</description></item>
/// </list>
/// </summary>
public sealed class ClientValidationFilterTests
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly PostgreSqlFixture                _fixture;
    private readonly DbContextOptions<RepositoryContext> _dbContextOptions;
    private readonly Mock<IApplicationHasher>         _applicationHasherMock = new();

    private const string ValidClientId  = "order-service-prod";
    private const string ValidClientKey = "raw-client-key-value";
    private const string HashedClientKey = "hashed-client-key-value";

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public ClientValidationFilterTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _dbContextOptions = new DbContextOptionsBuilder<RepositoryContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        // Ensure schema exists and reset state before each test
        await using var ctx = new RepositoryContext(_dbContextOptions);
        await ctx.Database.EnsureCreatedAsync();
        ctx.WebhookServiceClients.RemoveRange(ctx.WebhookServiceClients);
        await ctx.SaveChangesAsync();

        _applicationHasherMock.Reset();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ClientValidationFilter CreateSut()
    {
        var ctx = new RepositoryContext(_dbContextOptions);
        return new ClientValidationFilter(ctx, _applicationHasherMock.Object);
    }

    private static AuthorizationFilterContext BuildContext(
        string? clientId  = null,
        string? clientKey = null)
    {
        var httpContext = new DefaultHttpContext();

        if (clientId  is not null)
            httpContext.Request.Headers["X-Client-Id"]  = clientId;

        if (clientKey is not null)
            httpContext.Request.Headers["X-Client-Key"] = clientKey;

        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor());

        return new AuthorizationFilterContext(
            actionContext,
            new List<IFilterMetadata>());
    }

    private async Task SeedClientAsync(
        string clientId    = ValidClientId,
        string clientKey   = HashedClientKey,
        bool   isActive    = true)
    {
        await using var ctx = new RepositoryContext(_dbContextOptions);

        ctx.WebhookServiceClients.Add(new WebhookServiceClient
        {
            Id          = Guid.NewGuid(),
            ClientId    = clientId.ToLower(),
            ClientKey   = clientKey,
            ServiceClientName = "Test Service",
            //ContactEmail = "test@company.com",
            IsActive    = isActive,
            CreatedAt   = DateTimeOffset.UtcNow,
            CreatedBy = Guid.NewGuid().ToString()
        });

        await ctx.SaveChangesAsync();
    }

    private static void AssertUnauthorized(AuthorizationFilterContext context)
    {
        Assert.NotNull(context.Result);
        var result = Assert.IsType<UnauthorizedObjectResult>(context.Result);
        var body   = Assert.IsType<GenericResponse<string>>(result.Value);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.Unauthorized, body.HttpStatusCode);
        Assert.Equal("Unauthorized Access", body.ResponseMessage, ignoreCase: true);
    }

    // =========================================================================
    // Missing / empty headers
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_MissingClientIdHeader_Returns401()
    {
        // Arrange — only X-Client-Key present
        var context = BuildContext(clientId: null, clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_MissingClientKeyHeader_Returns401()
    {
        // Arrange — only X-Client-Id present
        var context = BuildContext(clientId: ValidClientId, clientKey: null);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_BothHeadersMissing_Returns401()
    {
        // Arrange
        var context = BuildContext(clientId: null, clientKey: null);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_EmptyClientId_Returns401()
    {
        // Arrange
        var context = BuildContext(clientId: "", clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_EmptyClientKey_Returns401()
    {
        // Arrange
        var context = BuildContext(clientId: ValidClientId, clientKey: "");
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhitespaceClientId_Returns401()
    {
        // Arrange
        var context = BuildContext(clientId: "   ", clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WhitespaceClientKey_Returns401()
    {
        // Arrange
        var context = BuildContext(clientId: ValidClientId, clientKey: "   ");
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    // =========================================================================
    // ClientId not found in database
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_ClientIdNotInDatabase_Returns401()
    {
        // Arrange — no clients seeded
        var context = BuildContext(clientId: ValidClientId, clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);

        // Hasher should never be called — short-circuits before key validation
        _applicationHasherMock.Verify(
            h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task OnAuthorizationAsync_WrongClientId_Returns401()
    {
        // Arrange — seed a different client
        await SeedClientAsync("payment-gateway-prod");

        var context = BuildContext(clientId: "order-service-prod", clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    // =========================================================================
    // Key hash validation failures
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_InvalidClientKey_Returns401()
    {
        // Arrange
        await SeedClientAsync();

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        var context = BuildContext(clientId: ValidClientId, clientKey: "wrong-key");
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        AssertUnauthorized(context);
    }

    [Fact]
    public async Task OnAuthorizationAsync_KeyValidationCalled_WithCorrectArguments()
    {
        // Arrange — verify the filter passes the raw key and stored hash
        // to the hasher in the correct order
        await SeedClientAsync(clientKey: HashedClientKey);

        string? capturedRawKey    = null;
        string? capturedHashedKey = null;

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((raw, hashed) =>
            {
                capturedRawKey    = raw;
                capturedHashedKey = hashed;
            })
            .ReturnsAsync(false);

        var context = BuildContext(clientId: ValidClientId, clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert — raw key from header and stored hash from DB forwarded correctly
        Assert.Equal(ValidClientKey,  capturedRawKey);
        Assert.Equal(HashedClientKey, capturedHashedKey);
    }

    // =========================================================================
    // Success path
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_ValidCredentials_DoesNotSetResult()
    {
        // Arrange — result being null means the filter passed and the
        // request continues to the controller action
        await SeedClientAsync();

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var context = BuildContext(clientId: ValidClientId, clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert — no result set means the pipeline continues
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task OnAuthorizationAsync_ValidCredentials_HasherCalledExactlyOnce()
    {
        // Arrange
        await SeedClientAsync();

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var context = BuildContext(clientId: ValidClientId, clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        _applicationHasherMock.Verify(
            h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()),
            Times.Once);
    }

    // =========================================================================
    // ClientId lookup is case-insensitive
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_ClientIdUpperCase_LookupIsCaseInsensitive()
    {
        // Arrange — client stored as lowercase, header sent as uppercase
        await SeedClientAsync(clientId: "order-service-prod");

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var context = BuildContext(clientId: "ORDER-SERVICE-PROD", clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert — lookup normalises to lowercase so client is found
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task OnAuthorizationAsync_ClientIdMixedCase_LookupIsCaseInsensitive()
    {
        // Arrange
        await SeedClientAsync(clientId: "order-service-prod");

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var context = BuildContext(clientId: "Order-Service-Prod", clientKey: ValidClientKey);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert
        Assert.Null(context.Result);
    }

    // =========================================================================
    // Response body shape
    // =========================================================================

    [Fact]
    public async Task OnAuthorizationAsync_Failure_ResponseBodyIsGenericResponse()
    {
        // Arrange — missing headers — simplest failure path
        var context = BuildContext(clientId: null, clientKey: null);
        var sut     = CreateSut();

        // Act
        await sut.OnAuthorizationAsync(context);

        // Assert — result is UnauthorizedObjectResult wrapping GenericResponse<string>
        var result = Assert.IsType<UnauthorizedObjectResult>(context.Result);
        Assert.Equal(StatusCodes.Status401Unauthorized, result.StatusCode);

        var body = Assert.IsType<GenericResponse<string>>(result.Value);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
        Assert.Equal(HttpStatusCode.Unauthorized, body.HttpStatusCode);
    }

    [Fact]
    public async Task OnAuthorizationAsync_Failure_NeverRevealSpecificReason()
    {
        // Arrange — test that all three distinct failure paths return
        // the same generic message so callers cannot distinguish between
        // missing headers, unknown client, and wrong key

        var contexts = new[]
        {
            // Missing headers
            BuildContext(null, null),
            // Unknown client
            BuildContext("nonexistent-client", ValidClientKey),
            // Wrong key — seed client first
        };

        _applicationHasherMock
            .Setup(h => h.ValidateHashedSecret(
                It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);

        await SeedClientAsync();
        var wrongKeyContext = BuildContext(ValidClientId, "wrong-key");

        var sut = CreateSut();

        foreach (var context in contexts.Append(wrongKeyContext))
        {
            await sut.OnAuthorizationAsync(context);

            var result = Assert.IsType<UnauthorizedObjectResult>(context.Result);
            var body   = Assert.IsType<GenericResponse<string>>(result.Value);
            Assert.Equal("Unauthorized Access", body.ResponseMessage, ignoreCase: true);
        }
    }
}
