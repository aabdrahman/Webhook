using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Net;
using WebHook.Api.Controllers;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookSubscriptionEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Tests.IntegrationTests;

public class WebhookSubscriptionEventControllerTests
{
    private readonly Mock<IWebhookSubscriptionEventService> _webhookSubscriptionServiceMock;
    private readonly WebhookSubscriptionEventController _webhookSubscriptionEventController;

    public WebhookSubscriptionEventControllerTests()
    {
        _webhookSubscriptionServiceMock = new Mock<IWebhookSubscriptionEventService>();
        _webhookSubscriptionEventController = new WebhookSubscriptionEventController(_webhookSubscriptionServiceMock.Object);
    }

    [Fact]
    public async Task GetSubscribedEvents_ThrowsException_Returns500InteranlServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetSubscribedEventsAsync(Guid.NewGuid(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("An error occurred."));


        //Act
        var result = await _webhookSubscriptionEventController.GetSubscribedEvents(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task GetSubscribedEvents_ValidRequest_Returns404NotFound()
    {
        //Arrange
        var expectedResponse = GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>.Failure(null, "No subscribed events found for the specified subscription.", HttpStatusCode.NotFound,
                                                        new ErrorDetail { ErrorMessage = "No subscribed events found.", ErrorTitle = "Not Found", ErrorDescription = $"The subscription with ID {Guid.NewGuid()} has no subscribed events." });
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetSubscribedEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.GetSubscribedEvents(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, body.HttpStatusCode);
    }

    [Fact]
    public async Task GetSubscribedEvents_ValidRequest_Returns200OK()
    {
        //Arrange
        var subscribedEvents = new List<WebhookSubscriptionEventDto>()
        {
            new WebhookSubscriptionEventDto(){ SubscriptionId = Guid.NewGuid(), SubscriptionName = "Event 1" },
            new WebhookSubscriptionEventDto(){ SubscriptionId = Guid.NewGuid(), SubscriptionName = "Event 2" },
            new WebhookSubscriptionEventDto(){ SubscriptionId = Guid.NewGuid(), SubscriptionName = "Event 3" }
        };
        var expectedResponse = GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>.Success(subscribedEvents, "Webhook Subscribed events fetched successfully.", HttpStatusCode.OK);
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetSubscribedEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.GetSubscribedEvents(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionEventDto>>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.NotNull(body.ResponseData);
        Assert.True(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, body.HttpStatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_ThrowsException_Returns500InteranlServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.SubscribeToEventAsync(Guid.NewGuid(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("An error occurred."));


        //Act
        var result = await _webhookSubscriptionEventController.SubscribeEvent(Guid.NewGuid(), "Test Event");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_Returns400BadRequest()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure(null, "Event does not exist.", HttpStatusCode.BadRequest);
        _webhookSubscriptionServiceMock.Setup(ws => ws.SubscribeToEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.SubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, body.HttpStatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_Returns409Conflict()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure(null, "Event already exists for the subscription.", HttpStatusCode.Conflict);
        _webhookSubscriptionServiceMock.Setup(ws => ws.SubscribeToEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.SubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status409Conflict, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, body.HttpStatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_Returns404NotFound()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure(null, "Subscription with Id does not exist.", HttpStatusCode.NotFound);
        _webhookSubscriptionServiceMock.Setup(ws => ws.SubscribeToEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.SubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, body.HttpStatusCode);
    }

    [Fact]
    public async Task SubscribeEvent_Returns200OK()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure(null, "Subscription with Id does not exist.", HttpStatusCode.OK);
        _webhookSubscriptionServiceMock.Setup(ws => ws.SubscribeToEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.SubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, body.HttpStatusCode);
    }

    [Fact]
    public async Task UnsubscribeEvent_ThrowsException_Returns500InteranlServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.UnsubscribeFromEventAsync(Guid.NewGuid(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("An error occurred."));


        //Act
        var result = await _webhookSubscriptionEventController.UnsubscribeEvent(Guid.NewGuid(), "Test Event");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task UnSubscribeEvent_Returns200OK()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Success(null, "Successfully unsubscribed from event.", HttpStatusCode.OK);
        _webhookSubscriptionServiceMock.Setup(ws => ws.UnsubscribeFromEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.UnsubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.True(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, body.HttpStatusCode);
    }

    [Fact]
    public async Task UnSubscribeEvent_Returns404NotFound()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure(null, "Subscription does not exist for event.", HttpStatusCode.NotFound);
        _webhookSubscriptionServiceMock.Setup(ws => ws.UnsubscribeFromEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionEventController.UnsubscribeEvent(Guid.NewGuid(), "Test 1");
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(objResult);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, body.HttpStatusCode);
    }
}
