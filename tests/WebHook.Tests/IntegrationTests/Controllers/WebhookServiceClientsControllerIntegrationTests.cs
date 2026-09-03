using Moq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookServiceClient;
using WebHook.Core.Interfaces.Services;
using WebHook.IntegrationTests.Controllers;

namespace WebHook.IntegrationTests.Controllers.ServiceClients;

/// <summary>
/// HTTP-level integration tests for <see cref="WebhookServiceClientsController"/>.
///
/// ENDPOINTS UNDER TEST:
/// <list type="bullet">
///   <item><description>GET    /api/WebhookServiceClients                          — GetAllOnboardedClients  [Admin]</description></item>
///   <item><description>GET    /api/WebhookServiceClients/{clientid}               — GetByClientId           [Admin]</description></item>
///   <item><description>POST   /api/WebhookServiceClients                          — OnboardClient           [Admin]</description></item>
///   <item><description>DELETE /api/WebhookServiceClients/deactivate/{clientid}    — DeactivateOnboardedClient [Admin]</description></item>
///   <item><description>PUT    /api/WebhookServiceClients/reactivate/{clientid}    — ReactivateOnboardedClient [Admin]</description></item>
///   <item><description>PUT    /api/WebhookServiceClients/request-new-key          — RequestNewClientKey     [Admin]</description></item>
/// </list>
/// </summary>
public sealed class WebhookServiceClientsControllerIntegrationTests
    : IClassFixture<WebApiFactory>, IAsyncLifetime
{
    private readonly WebApiFactory _factory;
    private HttpClient _client = null!;

    public WebhookServiceClientsControllerIntegrationTests(
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

    private static WebhookServiceClientDto BuildClientDto(
        string clientId     = "order-service-prod",
        string serviceName  = "Order Management Service",
        bool   isActive     = true) => new()
        {
            Id          = Guid.NewGuid(),
            ClientId    = clientId,
            ServiceClientName = serviceName,
            ActiveStatus    = isActive,
            CreatedAt   = DateTimeOffset.UtcNow,
            SubscribedCatalogs = ["customercreated", "ordercancelled"]
        };

    private static CreateServiceClientDto BuildCreateDto(
        string clientId     = "order-service-prod",
        string serviceName  = "Order Management Service") => new()
        {
            ClientId         = clientId,
            ServiceName      = serviceName,
            ContactEmail     = "platform@company.com",
            AllowedEventTypes = ["OrderCreated", "OrderCancelled"]
        };

    private static RequestNewClientKeyDto BuildRequestNewKeyDto(
        string clientId = "order-service-prod", string servicename = "Order Service") => new()
        {
            ClientId      = clientId,
            ServiceName = servicename
        };

    // =========================================================================
    // GetAllOnboardedClients — GET /api/WebhookServiceClients
    // =========================================================================

    [Fact]
    public async Task GetAllOnboardedClients_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/WebhookServiceClients");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAllOnboardedClients_NoClientsFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>
                .Failure(null, "No service clients found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetAllOnboardedClients_ClientsExist_Returns200()
    {
        // Arrange
        var clients = new List<WebhookServiceClientDto>
        {
            BuildClientDto("order-service-prod",   "Order Management Service"),
            BuildClientDto("payment-gateway-prod",  "Payment Gateway")
        };

        _factory.ServiceClientServiceMock
            .Setup(s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>
                .Success(clients, "Clients fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<IReadOnlyList<WebhookServiceClientDto>>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(clients.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetAllOnboardedClients_ForwardsIncludeDeactivatedQueryParam()
    {
        // Arrange
        bool? capturedIncludeDeactivated = null;

        _factory.ServiceClientServiceMock
            .Setup(s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<bool, CancellationToken>((flag, _) => capturedIncludeDeactivated = flag)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync("/api/WebhookServiceClients?includeDeactivated=true");

        // Assert
        Assert.True(capturedIncludeDeactivated);
    }

    [Fact]
    public async Task GetAllOnboardedClients_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task GetAllOnboardedClients_ServiceCalledExactlyOnce()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookServiceClientDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync("/api/WebhookServiceClients");

        // Assert
        _factory.ServiceClientServiceMock.Verify(
            s => s.GetAllClientsAsync(
                It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // GetByClientId — GET /api/WebhookServiceClients/{clientid}
    // =========================================================================

    [Fact]
    public async Task GetByClientId_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/api/WebhookServiceClients/order-service-prod");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetByClientId_ClientNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.GetByClientIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<WebhookServiceClientDto>
                .Failure(null, "Service client not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients/nonexistent-client");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<WebhookServiceClientDto>>();
        Assert.NotNull(body);
        Assert.False(body!.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetByClientId_ClientExists_Returns200()
    {
        // Arrange
        var client = BuildClientDto("order-service-prod", "Order Management Service");

        _factory.ServiceClientServiceMock
            .Setup(s => s.GetByClientIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<WebhookServiceClientDto>
                .Success(client, "Client fetched successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients/order-service-prod");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<WebhookServiceClientDto>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("order-service-prod", body.ResponseData.ClientId);
    }

    [Fact]
    public async Task GetByClientId_ForwardsClientIdToService()
    {
        // Arrange
        string? capturedClientId = null;

        _factory.ServiceClientServiceMock
            .Setup(s => s.GetByClientIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => capturedClientId = id)
            .ReturnsAsync(GenericResponse<WebhookServiceClientDto>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _client.GetAsync("/api/WebhookServiceClients/order-service-prod");

        // Assert
        Assert.Equal("order-service-prod", capturedClientId);
    }

    [Fact]
    public async Task GetByClientId_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.GetByClientIdAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.GetAsync("/api/WebhookServiceClients/order-service-prod");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // OnboardClient — POST /api/WebhookServiceClients
    // =========================================================================

    [Fact]
    public async Task OnboardClient_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PostAsJsonAsync(
            "/api/WebhookServiceClients", BuildCreateDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OnboardClient_ValidRequest_Returns201Created()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.OnboardNewServiceClientAsync(
                It.IsAny<CreateServiceClientDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>.Success(
                new ServiceClientOnboardingResponse
                {
                    ClientId  = "order-service-prod",
                    ClientKey = "raw-key-shown-once",
                    Message   = "Store your ClientKey securely. It will not be shown again."
                },
                "Service client onboarded successfully.",
                HttpStatusCode.Created));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookServiceClients", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<ServiceClientOnboardingResponse>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("order-service-prod",    body.ResponseData.ClientId);
        Assert.Equal("raw-key-shown-once",    body.ResponseData.ClientKey);
    }

    [Fact]
    public async Task OnboardClient_DuplicateClientId_Returns409Conflict()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.OnboardNewServiceClientAsync(
                It.IsAny<CreateServiceClientDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>
                .Failure(null, "A service client with ClientId 'order-service-prod' already exists.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookServiceClients", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task OnboardClient_InvalidEventTypes_Returns400()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.OnboardNewServiceClientAsync(
                It.IsAny<CreateServiceClientDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>
                .Failure(null, "The following event types do not exist: NonExistentEvent", HttpStatusCode.BadRequest));

        // Act
        var request = BuildCreateDto();
        request.AllowedEventTypes = ["NonExistentEvent"];
        var response = await _client.PostAsJsonAsync("/api/WebhookServiceClients", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OnboardClient_ForwardsRequestBodyToService()
    {
        // Arrange
        CreateServiceClientDto? captured = null;
        var request = BuildCreateDto("payment-gateway-prod", "Payment Gateway");

        _factory.ServiceClientServiceMock
            .Setup(s => s.OnboardNewServiceClientAsync(
                It.IsAny<CreateServiceClientDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateServiceClientDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>
                .Success(new ServiceClientOnboardingResponse(), "OK.", HttpStatusCode.Created));

        // Act
        await _client.PostAsJsonAsync("/api/WebhookServiceClients", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("payment-gateway-prod", captured!.ClientId);
        Assert.Equal("Payment Gateway",       captured.ServiceName);
    }

    [Fact]
    public async Task OnboardClient_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.OnboardNewServiceClientAsync(
                It.IsAny<CreateServiceClientDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/WebhookServiceClients", BuildCreateDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // DeactivateOnboardedClient — DELETE /api/WebhookServiceClients/deactivate/{clientid}
    // =========================================================================

    [Fact]
    public async Task DeactivateOnboardedClient_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/order-service-prod");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateOnboardedClient_ClientExists_Returns204()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.DeactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Service client deactivated successfully.", HttpStatusCode.NoContent));

        // Act
        var response = await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/order-service-prod");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateOnboardedClient_ClientNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.DeactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Service client not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/nonexistent-client");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateOnboardedClient_AlreadyDeactivated_Returns403()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.DeactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Service client is already deactivated.", HttpStatusCode.Forbidden));

        // Act
        var response = await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/order-service-prod");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateOnboardedClient_ForwardsClientIdToService()
    {
        // Arrange
        string? capturedClientId = null;

        _factory.ServiceClientServiceMock
            .Setup(s => s.DeactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => capturedClientId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Deactivated.", HttpStatusCode.NoContent));

        // Act
        await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/order-service-prod");

        // Assert
        Assert.Equal("order-service-prod", capturedClientId);
    }

    [Fact]
    public async Task DeactivateOnboardedClient_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.DeactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.DeleteAsync(
            "/api/WebhookServiceClients/deactivate/order-service-prod");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // ReactivateOnboardedClient — PUT /api/WebhookServiceClients/reactivate/{clientid}
    // =========================================================================

    [Fact]
    public async Task ReactivateOnboardedClient_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/order-service-prod", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateOnboardedClient_ClientExists_Returns200()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.ReactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Service client reactivated successfully.", HttpStatusCode.OK));

        // Act
        var response = await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/order-service-prod", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateOnboardedClient_ClientNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.ReactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Service client not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/nonexistent-client", null);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateOnboardedClient_AlreadyActive_Returns409Conflict()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.ReactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure(null, "Service client is already active.", HttpStatusCode.Conflict));

        // Act
        var response = await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/order-service-prod", null);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ReactivateOnboardedClient_ForwardsClientIdToService()
    {
        // Arrange
        string? capturedClientId = null;

        _factory.ServiceClientServiceMock
            .Setup(s => s.ReactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((id, _) => capturedClientId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Reactivated.", HttpStatusCode.OK));

        // Act
        await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/order-service-prod", null);

        // Assert
        Assert.Equal("order-service-prod", capturedClientId);
    }

    [Fact]
    public async Task ReactivateOnboardedClient_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.ReactivateClientAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PutAsync(
            "/api/WebhookServiceClients/reactivate/order-service-prod", null);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    // =========================================================================
    // RequestNewClientKey — PUT /api/WebhookServiceClients/request-new-key
    // =========================================================================

    [Fact]
    public async Task RequestNewClientKey_NoAuthToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.PutAsJsonAsync(
            "/api/WebhookServiceClients/request-new-key", BuildRequestNewKeyDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RequestNewClientKey_ValidRequest_Returns200()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.RequestNewClientKeyAsync(
                It.IsAny<RequestNewClientKeyDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>.Success(
                new ServiceClientOnboardingResponse
                {
                    ClientId  = "order-service-prod",
                    ClientKey = "new-raw-key-shown-once",
                    Message   = "Store your ClientKey securely. It will not be shown again."
                },
                "New client key issued successfully.",
                HttpStatusCode.OK));

        // Act
        var response = await _client.PutAsJsonAsync(
            "/api/WebhookServiceClients/request-new-key", BuildRequestNewKeyDto());

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<GenericResponse<ServiceClientOnboardingResponse>>();
        Assert.NotNull(body);
        Assert.True(body!.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal("new-raw-key-shown-once", body.ResponseData.ClientKey);
    }

    [Fact]
    public async Task RequestNewClientKey_ClientNotFound_Returns404()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.RequestNewClientKeyAsync(
                It.IsAny<RequestNewClientKeyDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>
                .Failure(null, "Service client not found.", HttpStatusCode.NotFound));

        // Act
        var response = await _client.PutAsJsonAsync(
            "/api/WebhookServiceClients/request-new-key", BuildRequestNewKeyDto());

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RequestNewClientKey_ForwardsRequestBodyToService()
    {
        // Arrange
        RequestNewClientKeyDto? captured = null;

        _factory.ServiceClientServiceMock
            .Setup(s => s.RequestNewClientKeyAsync(
                It.IsAny<RequestNewClientKeyDto>(), It.IsAny<CancellationToken>()))
            .Callback<RequestNewClientKeyDto, CancellationToken>(
                (dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<ServiceClientOnboardingResponse>
                .Success(new ServiceClientOnboardingResponse(), "OK.", HttpStatusCode.OK));

        var request = BuildRequestNewKeyDto("payment-gateway-prod");

        // Act
        await _client.PutAsJsonAsync(
            "/api/WebhookServiceClients/request-new-key", request);

        // Assert
        Assert.NotNull(captured);
        Assert.Equal("payment-gateway-prod", captured!.ClientId);
    }

    [Fact]
    public async Task RequestNewClientKey_ServiceThrowsException_Returns500()
    {
        // Arrange
        _factory.ServiceClientServiceMock
            .Setup(s => s.RequestNewClientKeyAsync(
                It.IsAny<RequestNewClientKeyDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Unexpected fault."));

        // Act
        var response = await _client.PutAsJsonAsync(
            "/api/WebhookServiceClients/request-new-key", BuildRequestNewKeyDto());

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
