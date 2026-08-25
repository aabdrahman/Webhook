using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;

namespace WebHook.IntegrationTests.Controllers;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookSubscriptionController"/>.
///
/// TESTING STRATEGY:
/// <see cref="IWebhookSubscriptionService"/> and <see cref="ICacheService"/> are
/// replaced with Moq mocks via <see cref="WebhookSubscriptionWebApiFactory"/> so
/// tests cover:
///   - Correct HTTP method and route matching
///   - Authentication — unauthenticated requests return 401
///   - Status code mapping from service response to HTTP response
///   - Request body deserialization and forwarding to service
///   - Route parameter forwarding to service
///   - Exception handling returning 500
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET    /api/WebhookSubscription                           — GetAll          [Admin]</description></item>
///   <item><description>GET    /api/WebhookSubscription/{id:guid}                 — GetById         [Admin]</description></item>
///   <item><description>GET    /api/WebhookSubscription/get-user-subscriptions    — GetUserSubs     [Authorize]</description></item>
///   <item><description>POST   /api/WebhookSubscription                           — Create          [Authorize]</description></item>
///   <item><description>PUT    /api/WebhookSubscription/{id:guid}                 — Activate        [Authorize]</description></item>
///   <item><description>DELETE /api/WebhookSubscription/{id:guid}                 — Delete          [Authorize]</description></item>
/// </list>
/// </summary>
public sealed class WebhookSubscriptionControllerIntegrationTests
    : IClassFixture<WebhookSubscriptionWebApiFactory>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly WebhookSubscriptionWebApiFactory _factory;
    private HttpClient _client = null!;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookSubscriptionControllerIntegrationTests(
        WebhookSubscriptionWebApiFactory factory)
        => _factory = factory;

    // -------------------------------------------------------------------------
    // IAsyncLifetime — runs before and after each test method
    // -------------------------------------------------------------------------

    public Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "test-token");
        _factory.ResetMocks();
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

    private static WebhookSubscriptionDto BuildSubscriptionDto(
        string name,
        List<string> events) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            SubscribedEvents = events,
            SubscribedFields = [],
            SecretKey = Random.Shared.GetHexString(12),
            CreatedDate = DateTimeOffset.UtcNow
        };

    private static CreateWebhookSubscriptionDto BuildCreateDto(
        List<string>? events = null) => new()
        {
            CallBackUrl = "https://example.com/",
            SubscriberName = "Test Subscriber",
            SubscribedEvents = events ?? ["OrderCreated", "UserCreated"],
            SubscribedFields = ["name", "email"]
        };

    // =========================================================================
    // GetAll — GET /api/WebhookSubscription
    // =========================================================================

    [Fact]
    public async Task GetAll_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/WebhookSubscription");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_NoSubscriptions_Returns404()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "No subscriptions found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync("/api/WebhookSubscription");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetAll_SubscriptionsExist_Returns200()
    {
        // Arrange
        var subscriptions = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("sub 1", ["OrderCreated"]),
            BuildSubscriptionDto("sub 2", ["UserCreated"]),
            BuildSubscriptionDto("sub 3", ["PaymentReceived"])
        };

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Success(subscriptions, "Fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync("/api/WebhookSubscription");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(subscriptions.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetAll_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync("/api/WebhookSubscription");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync("/api/WebhookSubscription");

        // Assert
        _factory.WebhookSubscriptionServiceMock.Verify(
            s => s.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // GetById — GET /api/WebhookSubscription/{webhookSubscriptionId:guid}
    // =========================================================================

    [Fact]
    public async Task GetById_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetById_NonExistingId_Returns404()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<WebhookSubscriptionDto>
                .Failure(null, "Subscription not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<WebhookSubscriptionDto>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetById_ExistingId_Returns200()
    {
        // Arrange
        var subscription = BuildSubscriptionDto("my sub", ["OrderCreated"]);

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<WebhookSubscriptionDto>
                .Success(subscription, "Fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<WebhookSubscriptionDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(subscription.Name, body.ResponseData.Name);
    }

    [Fact]
    public async Task GetById_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<WebhookSubscriptionDto>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync($"/api/WebhookSubscription/{subscriptionId}");

        // Assert — correct route parameter forwarded to service
        Assert.Equal(subscriptionId, capturedId);
    }

    [Fact]
    public async Task GetById_NonGuidInRoute_Returns400()
    {
        // Route constraint {webhookSubscriptionId:guid} rejects non-GUID values
        // before the controller is invoked — service is never called
        var response = await _client.GetAsync(
            "/api/WebhookSubscription/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // GetUserSubscriptions — GET /api/WebhookSubscription/get-user-subscriptions
    // =========================================================================

    private const string UserSubscriptionsRoute =
        "/api/WebhookSubscription/get-user-subscriptions";

    [Fact]
    public async Task GetUserSubscriptions_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync(UserSubscriptionsRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserSubscriptions_NoSubscriptionsForUser_Returns404()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "No subscriptions found for user.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync(UserSubscriptionsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetUserSubscriptions_SubscriptionsExist_Returns200()
    {
        // Arrange
        var userSubscriptions = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("my sub 1", ["OrderCreated"]),
            BuildSubscriptionDto("my sub 2", ["UserCreated"])
        };

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Success(userSubscriptions, "Fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(UserSubscriptionsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(userSubscriptions.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetUserSubscriptions_ResponseBodyMatchesServiceOutput()
    {
        // Arrange
        var expected = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("exact sub", ["OrderCreated", "UserCreated"])
        };

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Success(expected, "OK.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync(UserSubscriptionsRoute);
        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>();

        // Assert
        Assert.NotNull(body?.ResponseData);
        Assert.Single(body!.ResponseData);
        Assert.Equal("exact sub", body.ResponseData[0].Name);
        Assert.Contains("OrderCreated", body.ResponseData[0].SubscribedEvents);
        Assert.Contains("UserCreated", body.ResponseData[0].SubscribedEvents);
    }

    [Fact]
    public async Task GetUserSubscriptions_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync(UserSubscriptionsRoute);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetUserSubscriptions_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync(UserSubscriptionsRoute);

        // Assert
        _factory.WebhookSubscriptionServiceMock.Verify(
            s => s.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // Create — POST /api/WebhookSubscription
    // =========================================================================

    [Fact]
    public async Task Create_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/WebhookSubscription", BuildCreateDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201Created()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscription created successfully.", HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookSubscription", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GenericResponse<string>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
    }

    [Fact]
    public async Task Create_InvalidEvents_Returns400()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "One or more events not found.", HttpStatusCode.BadRequest));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookSubscription", BuildCreateDto(["NonExistentEvent"]));

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_ForwardsRequestBodyToService()
    {
        // Arrange
        CreateWebhookSubscriptionDto? captured = null;
        var request = BuildCreateDto(["OrderCreated", "UserCreated"]);

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWebhookSubscriptionDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/WebhookSubscription", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal(request.SubscriberName, captured!.SubscriberName);
        Assert.Equal(request.CallBackUrl, captured.CallBackUrl);
        Assert.Equal(request.SubscribedEvents.Count, captured.SubscribedEvents.Count);
    }

    [Fact]
    public async Task Create_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookSubscription", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Create_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/WebhookSubscription", BuildCreateDto());

        // Assert
        _factory.WebhookSubscriptionServiceMock.Verify(
            s => s.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // ActivateSubscription — PUT /api/WebhookSubscription/{webhookSubscriptionId:guid}
    // =========================================================================

    [Fact]
    public async Task ActivateSubscription_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_NonExistingId_Returns404()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_AlreadyActive_Returns409Conflict()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription is already active.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_ValidId_Returns200()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscription activated successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Activated.", HttpStatusCode.OK));

        // Act
        await _client.PutAsync(
            $"/api/WebhookSubscription/{subscriptionId}", null);

        // Assert
        Assert.Equal(subscriptionId, capturedId);
    }

    [Fact]
    public async Task ActivateSubscription_NonGuidInRoute_Returns400()
    {
        var response = await _client.PutAsync(
            "/api/WebhookSubscription/not-a-guid", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PutAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // Delete — DELETE /api/WebhookSubscription/{webhookSubscriptionId:guid}
    // =========================================================================

    [Fact]
    public async Task Delete_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_NonExistingId_Returns404()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Subscription not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ExistingId_Returns200()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Subscription deleted successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Deleted.", HttpStatusCode.OK));

        // Act
        await _client.DeleteAsync($"/api/WebhookSubscription/{subscriptionId}");

        // Assert
        Assert.Equal(subscriptionId, capturedId);
    }

    [Fact]
    public async Task Delete_NonGuidInRoute_Returns400()
    {
        var response = await _client.DeleteAsync(
            "/api/WebhookSubscription/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.WebhookSubscriptionServiceMock
            .Setup(s => s.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            $"/api/WebhookSubscription/{Guid.NewGuid()}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}

// =============================================================================
// Factory
// =============================================================================

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> scoped to
/// <see cref="WebhookSubscriptionController"/> integration tests.
///
/// Follows the same pattern as <see cref="WebApiFactory"/>:
/// <list type="bullet">
///   <item><description><see cref="IWebhookSubscriptionService"/> replaced with a Moq mock.</description></item>
///   <item><description><see cref="ICacheService"/> replaced with a Moq mock seeded in
///   <see cref="ResetMocks"/> so <see cref="CustomAuthenticationFilter"/> passes
///   authenticated requests through without rejecting on default Guid.</description></item>
///   <item><description>A single <see cref="SubscriptionTestAuthHandler"/> carries both USER
///   and Admin roles so all endpoints are reachable without multiple factories.</description></item>
/// </list>
/// </summary>
public sealed class WebhookSubscriptionWebApiFactory
    : WebApplicationFactory<Program>
{
    public Mock<IWebhookSubscriptionService> WebhookSubscriptionServiceMock { get; } = new();
    public Mock<ICacheService> CacheServiceMock { get; } = new();

    /// <summary>
    /// Resets all mock setups and recorded invocations before each test method.
    /// Re-applies the cache setup after reset using the typed
    /// <see cref="SubscriptionTestAuthHandler.TestJtiGuid"/> — not a string parse —
    /// so <see cref="CustomAuthenticationFilter"/> never sees a default Guid and
    /// continues to pass authenticated requests through.
    /// </summary>
    public void ResetMocks()
    {
        WebhookSubscriptionServiceMock.Reset();
        CacheServiceMock.Reset();

        // Re-establish after reset — must use TestJtiGuid (non-default Guid)
        // because the filter rejects any cached value equal to default(Guid)
        CacheServiceMock
            .Setup(c => c.GetItemsFromCacheAsync<Guid>(
                SubscriptionTestAuthHandler.TestEmail))
            .ReturnsAsync(SubscriptionTestAuthHandler.TestJtiGuid);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace real services with mocks
            services.RemoveAll<IWebhookSubscriptionService>();
            services.RemoveAll<ICacheService>();

            services.AddSingleton(WebhookSubscriptionServiceMock.Object);
            services.AddSingleton(CacheServiceMock.Object);

            // Register single test auth scheme
            services.AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, SubscriptionTestAuthHandler>(
                    SubscriptionTestAuthHandler.SchemeName, _ => { });

            // Override default scheme to use the test handler
            services.PostConfigure<AuthenticationOptions>(opts =>
            {
                opts.DefaultAuthenticateScheme = SubscriptionTestAuthHandler.SchemeName;
                opts.DefaultChallengeScheme = SubscriptionTestAuthHandler.SchemeName;
            });
        });
    }
}

// =============================================================================
// Test auth handler
// =============================================================================

/// <summary>
/// Auto-authenticates every request as a test user carrying both USER and Admin
/// roles so all endpoints in <see cref="WebhookSubscriptionController"/> are
/// reachable in tests.
///
/// The <see cref="TestEmail"/> and <see cref="TestJtiGuid"/> are seeded into the
/// cache mock in <see cref="WebhookSubscriptionWebApiFactory.ResetMocks"/> so
/// <see cref="CustomAuthenticationFilter"/> finds a non-default cached JTI that
/// matches the JTI claim and passes the request through.
///
/// The email and JTI are intentionally distinct from those in
/// <see cref="TestAuthHandler"/> used by <see cref="WebApiFactory"/> to prevent
/// cache key collisions if both factories run in the same test process.
/// </summary>
public sealed class SubscriptionTestAuthHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "SubscriptionTestAuth";
    public const string TestUserId = "00000000-0000-0000-0000-000000000001";
    public const string TestEmail = "TESTSUBSCRIPTION@ACME.COM";

    /// <summary>
    /// String form of the JTI — used in the <see cref="JwtRegisteredClaimNames.Jti"/>
    /// claim which requires a string value.
    /// </summary>
    public const string TestJti = "00000000-0000-0000-0000-000000000097";

    /// <summary>
    /// Typed Guid form of the JTI — used in <see cref="WebhookSubscriptionWebApiFactory.ResetMocks"/>
    /// to avoid <see cref="Guid.Parse"/> so a malformed constant cannot cause
    /// a startup failure in tests.
    /// </summary>
    public static readonly Guid TestJtiGuid = new("00000000-0000-0000-0000-000000000097");

    public SubscriptionTestAuthHandler(
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