using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Net;
using WebHook.Api.Controllers;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscription;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Tests.IntegrationTests;

public class WebhookSubscriptionControllerTests
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly Mock<IWebhookSubscriptionService> _webhookSubscriptionServiceMock;
    private readonly WebhookSubscriptionController _webhookSubscriptionController;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    public WebhookSubscriptionControllerTests()
    {
        _webhookSubscriptionServiceMock = new Mock<IWebhookSubscriptionService>();
        _webhookSubscriptionController = new WebhookSubscriptionController(
            _webhookSubscriptionServiceMock.Object);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static WebhookSubscriptionDto BuildSubscriptionDto(
        string name,
        List<string> subscribedEvents) => new()
        {
            CreatedDate = DateTimeOffset.UtcNow,
            Id = Guid.NewGuid(),
            Name = name,
            SubscribedFields = [],
            SubscribedEvents = subscribedEvents,
            SecretKey = Random.Shared.GetHexString(12)
        };

    private static CreateWebhookSubscriptionDto BuildCreateSubscription(
        List<string> events) => new()
        {
            CallBackUrl = "https://example.com",
            SubscriberName = "Test 1",
            SubscribedEvents = events,
            SubscribedFields = ["name"]
        };

    // =========================================================================
    // GetAll — GET /api/WebhookSubscription
    // =========================================================================

    [Fact]
    public async Task GetAll_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.GetAll();
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body.ErrorDetail);
    }

    [Fact]
    public async Task GetAll_NoSubscription_Returns404NotFound()
    {
        // Arrange
        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
            .Failure(null, "No webhook subscription found.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetAll();
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetAll_SubscriptionExists_Returns200OK()
    {
        // Arrange
        var events = Enumerable.Range(1, 10)
            .Select(_ => Random.Shared.GetHexString(6))
            .ToList();

        var subscriptions = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("test 1", events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList()),
            BuildSubscriptionDto("test 2", events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList()),
            BuildSubscriptionDto("test 3", events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList())
        };

        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
            .Success(subscriptions, "Subscriptions fetched successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetAll();
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(subscriptions.Count, body.ResponseData.Count);
        Assert.True(body.ResponseData.Any());
    }

    [Fact]
    public async Task GetAll_ServiceCalledExactlyOnce()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _webhookSubscriptionController.GetAll();

        // Assert
        _webhookSubscriptionServiceMock.Verify(
            ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // =========================================================================
    // GetUserSubscriptions — GET /api/WebhookSubscription/user
    // =========================================================================

    [Fact]
    public async Task GetUserSubscriptions_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.GetUserSubscriptions(CancellationToken.None);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body.ErrorDetail);
    }

    [Fact]
    public async Task GetUserSubscriptions_NoSubscriptionsForUser_Returns404NotFound()
    {
        // Arrange
        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
            .Failure(null, "No subscriptions found for user.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetUserSubscriptions(CancellationToken.None);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetUserSubscriptions_SubscriptionsExist_Returns200OK()
    {
        // Arrange
        var events = Enumerable.Range(1, 5)
            .Select(_ => Random.Shared.GetHexString(6))
            .ToList();

        var userSubscriptions = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("my subscription 1", events.Take(2).ToList()),
            BuildSubscriptionDto("my subscription 2", events.Take(3).ToList())
        };

        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
            .Success(userSubscriptions, "User subscriptions fetched successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetUserSubscriptions(CancellationToken.None);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(userSubscriptions.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetUserSubscriptions_ResponseContainsOnlyUserSubscriptions()
    {
        // Arrange — verify the response data matches exactly what the service returned
        var userSubscriptions = new List<WebhookSubscriptionDto>
        {
            BuildSubscriptionDto("my subscription", ["OrderCreated", "UserCreated"])
        };

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Success(userSubscriptions, "OK.", HttpStatusCode.OK));

        // Act
        var result = await _webhookSubscriptionController.GetUserSubscriptions(CancellationToken.None);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = Assert.IsType<GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>>(objResult.Value);

        // Assert — response data matches the service output exactly
        Assert.NotNull(body.ResponseData);
        Assert.Single(body.ResponseData);
        Assert.Equal("my subscription", body.ResponseData[0].Name);
        Assert.Contains("OrderCreated", body.ResponseData[0].SubscribedEvents);
        Assert.Contains("UserCreated", body.ResponseData[0].SubscribedEvents);
    }

    [Fact]
    public async Task GetUserSubscriptions_ServiceCalledExactlyOnce()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _webhookSubscriptionController.GetUserSubscriptions(CancellationToken.None);

        // Assert
        _webhookSubscriptionServiceMock.Verify(
            ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetUserSubscriptions_CancellationTokenForwardedToService()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken passed = CancellationToken.None;

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetUserSubscriptionsAsync(It.IsAny<CancellationToken>()))
            .Callback<CancellationToken>(ct => passed = ct)
            .ReturnsAsync(GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _webhookSubscriptionController.GetUserSubscriptions(cts.Token);

        // Assert — the exact token passed by the caller reached the service
        Assert.Equal(cts.Token, passed);
    }

    // =========================================================================
    // GetById — GET /api/WebhookSubscription/{id}
    // =========================================================================

    [Fact]
    public async Task GetById_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body.ErrorDetail);
    }

    [Fact]
    public async Task GetById_InvalidSubscriptionId_Returns404NotFound()
    {
        // Arrange
        var expectedResult = GenericResponse<WebhookSubscriptionDto>
            .Failure(null, "Subscription with id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<WebhookSubscriptionDto>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetById_SubscriptionExists_Returns200OK()
    {
        // Arrange
        var subscription = BuildSubscriptionDto("test 1", ["name", "email"]);
        var expectedResult = GenericResponse<WebhookSubscriptionDto>
            .Success(subscription, "Webhook subscription fetched successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<WebhookSubscriptionDto>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(subscription.Name, body.ResponseData.Name, ignoreCase: true);
    }

    [Fact]
    public async Task GetById_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.GetWebhookSubscriptionByIdAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<WebhookSubscriptionDto>
                .Failure(null, "Not found.", HttpStatusCode.NotFound));

        // Act
        await _webhookSubscriptionController.GetById(subscriptionId);

        // Assert — the correct id was forwarded to the service
        Assert.Equal(subscriptionId, capturedId);
    }

    // =========================================================================
    // Delete — DELETE /api/WebhookSubscription/{id}
    // =========================================================================

    [Fact]
    public async Task Delete_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task Delete_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        // Arrange
        var expectedResponse = GenericResponse<string>
            .Failure("Operation Failed.", "Subscription id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task Delete_SubscriptionExists_Returns200OK()
    {
        // Arrange
        var expectedResponse = GenericResponse<string>
            .Success("Operation Successful.", "Subscription deleted successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
    }

    [Fact]
    public async Task Delete_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.DeleteWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Deleted.", HttpStatusCode.OK));

        // Act
        await _webhookSubscriptionController.Delete(subscriptionId);

        // Assert
        Assert.Equal(subscriptionId, capturedId);
    }

    // =========================================================================
    // ActivateSubscription — PATCH /api/WebhookSubscription/{id}/activate
    // =========================================================================

    [Fact]
    public async Task ActivateSubscription_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        _webhookSubscriptionServiceMock
            .Setup(ws => ws.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        // Arrange
        var expectedResponse = GenericResponse<string>
            .Failure("Operation Failed.", "Subscription id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task ActivateSubscription_AlreadyActive_Returns409Conflict()
    {
        // Arrange
        var expectedResponse = GenericResponse<string>
            .Failure("Operation Failed.", "Subscription is already active.", HttpStatusCode.Conflict);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status409Conflict, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task ActivateSubscription_SubscriptionExists_Returns200OK()
    {
        // Arrange
        var expectedResponse = GenericResponse<string>
            .Success("Operation Successful.", "Subscription activated successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_ForwardsIdToService()
    {
        // Arrange
        var subscriptionId = Guid.NewGuid();
        var capturedId = Guid.Empty;

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.ActivateWebhookSubscriptionAsync(
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => capturedId = id)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Activated.", HttpStatusCode.OK));

        // Act
        await _webhookSubscriptionController.ActivateSubscription(subscriptionId);

        // Assert
        Assert.Equal(subscriptionId, capturedId);
    }

    // =========================================================================
    // Create — POST /api/WebhookSubscription
    // =========================================================================

    [Fact]
    public async Task Create_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        var events = Enumerable.Range(1, 10).Select(_ => Random.Shared.GetHexString(6)).ToList();
        var request = BuildCreateSubscription(
            events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result = await _webhookSubscriptionController.Create(request);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidEvents_Returns400BadRequest()
    {
        // Arrange
        var events = Enumerable.Range(1, 10).Select(_ => Random.Shared.GetHexString(6)).ToList();
        var request = BuildCreateSubscription(
            events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());

        request.SubscribedEvents.Add("NonExistentEvent");

        var expectedResult = GenericResponse<string>
            .Failure("Operation Failed.", "One or more events not found.", HttpStatusCode.BadRequest);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _webhookSubscriptionController.Create(request);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
    }

    [Fact]
    public async Task Create_ValidRequest_Returns201Created()
    {
        // Arrange
        var events = Enumerable.Range(1, 10).Select(_ => Random.Shared.GetHexString(6)).ToList();
        var request = BuildCreateSubscription(
            events.OrderBy(_ => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());

        var expectedResponse = GenericResponse<string>
            .Success("Operation Successful.", "Subscription created successfully.", HttpStatusCode.Created);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _webhookSubscriptionController.Create(request);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status201Created, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
    }

    [Fact]
    public async Task Create_ForwardsRequestBodyToService()
    {
        // Arrange
        var request = BuildCreateSubscription(["OrderCreated", "UserCreated"]);
        CreateWebhookSubscriptionDto? captured = null;

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateWebhookSubscriptionDto, CancellationToken>((dto, _) => captured = dto)
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _webhookSubscriptionController.Create(request);

        // Assert — correct request body forwarded to service
        Assert.NotNull(captured);
        Assert.Equal(request.SubscriberName, captured!.SubscriberName);
        Assert.Equal(request.CallBackUrl, captured.CallBackUrl);
        Assert.Equal(request.SubscribedEvents.Count, captured.SubscribedEvents.Count);
    }

    [Fact]
    public async Task Create_ServiceCalledExactlyOnce()
    {
        // Arrange
        var request = BuildCreateSubscription(["OrderCreated"]);

        _webhookSubscriptionServiceMock
            .Setup(ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "Created.", HttpStatusCode.Created));

        // Act
        await _webhookSubscriptionController.Create(request);

        // Assert
        _webhookSubscriptionServiceMock.Verify(
            ws => ws.CreateWebhookSubscriptionAsync(
                It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}