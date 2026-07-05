using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Net;
using WebHook.Api.Controllers;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Tests.IntegrationTests;

public class WebhookEventControllerTests
{
    private readonly Mock<IWebhookEventService> _mockWebhookEventService;
    private readonly WebhookEventController _controller;

    public WebhookEventControllerTests()
    {
        _mockWebhookEventService = new Mock<IWebhookEventService>();
        _controller = new WebhookEventController(_mockWebhookEventService.Object);
    }

    private Guid GenerateRandomGuid() => Guid.NewGuid();
    //private WebhookEventDto GenerateRandomWebhookEventDto()
    //{
    //    return new WebhookEventDto
    //    {
    //        CorrelationId = GenerateRandomGuid(),
    //        EventType = "TestEvent",
    //        PayLoad = "{}",
    //        CreatedAt = DateTime.UtcNow
    //    };
    //}

    private CreateWebhookEventDto BuildCreateWebhookEventDto(string eventType = "CustomerCreated", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}",
                                                            string source = "TestSource", Guid? correlationId = null) => new CreateWebhookEventDto()
    {
        EventType = eventType,
        PayLoad = payload,
        Source = source,
        CorrelationId = correlationId ?? Guid.NewGuid()
    };

    private GetWebhookEventParameters GetWebhookEventParameters(string eventType = "CustomerCreated", string source = "TestSource", 
                                                                WebHookEventStatus? status = null, int pageNumber = 1, 
                                                                int pageSize = 10, Guid? correlationId = null) => new GetWebhookEventParameters()
    {
        EventType = eventType,
        Source = source,
        Status = status?.ToString() ?? string.Empty,
        CorrelationId = correlationId,
    };

    public CreateWebhookEventDto GetCreateWebhookEventDto(string eventType = "", string payLoad = "", string source = "", Guid? correlationId = null) => new CreateWebhookEventDto()
    {
        CorrelationId = Guid.NewGuid(),
        Source = source,
        EventType = eventType,
        PayLoad = payLoad
    };

    private WebhookEventDto BuildWebhookEventEntityDto(string eventType = "CustomerCreated", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}",
                                            string source = "TestSource", WebHookEventStatus? status = null, Guid? correlationId = null) => new WebhookEventDto()
                                            {
                                                Id = Guid.NewGuid(),
                                                EventType = eventType,
                                                PayLoad = payload,
                                                Source = source,
                                                CorrelationId = correlationId ?? Guid.NewGuid(),
                                                Status = status.ToString() ?? WebHook.Core.Constants.WebHookEventStatus.Pending.ToString(),
                                                CreatedAt = DateTimeOffset.UtcNow
                                            };

    [Fact]
    public async Task GetWebhookEventAsync_CancellationRequested_ThrowsException500InternalServerError()
    {
        //Arrange
        var correlationId = GenerateRandomGuid();
        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel(); // Cancel the token immediately
        _mockWebhookEventService.Setup(service => service.GetWebhookEventAsync(correlationId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("The operation was canceled."));

        // Act
        var result = await _controller.GetEventByCorrelationId(correlationId);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetWebhookEventAsync_ValidCorrelationId_ReturnsOkResultWithWebhookEvent()
    {
        // Arrange
        var correlationId = GenerateRandomGuid();
        var expectedWebhookEvent = BuildWebhookEventEntityDto(correlationId: correlationId);

        var expectedResponse = GenericResponse<IReadOnlyList<WebhookEventDto>>.Success(
        
            new List<WebhookEventDto> { expectedWebhookEvent },
            "Webhook event fetched successfully.",
            System.Net.HttpStatusCode.OK
        );

        _mockWebhookEventService.Setup(service => service.GetWebhookEventAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);
        // Act
        var result = await _controller.GetEventByCorrelationId(correlationId);
        // Assert
        var okResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);
        var responseValue = Assert.IsType<GenericResponse<IReadOnlyList<WebhookEventDto>>>(okResult.Value);
        Assert.Equal(expectedResponse.ResponseMessage, responseValue.ResponseMessage);
        Assert.Equal(expectedResponse.HttpStatusCode, responseValue.HttpStatusCode);
        Assert.NotNull(responseValue.ResponseData);
        Assert.Single(responseValue.ResponseData);
        Assert.Equal(expectedWebhookEvent.CorrelationId, responseValue.ResponseData.First().CorrelationId);
    }

    [Fact]
    public async Task GetWebhookEventAsync_InvalidCorrelationId_ReturnsNotFoundResult()
    {
        // Arrange
        var correlationId = GenerateRandomGuid();
        var expectedResponse = GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(
            null,
            "Webhook event not found.",
            System.Net.HttpStatusCode.NotFound
        );
        _mockWebhookEventService.Setup(service => service.GetWebhookEventAsync(correlationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);
        // Act
        var result = await _controller.GetEventByCorrelationId(correlationId);
        // Assert
        var notFoundResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFoundResult.StatusCode);
        var responseValue = Assert.IsType<GenericResponse<IReadOnlyList<WebhookEventDto>>>(notFoundResult.Value);
        Assert.Equal(expectedResponse.ResponseMessage, responseValue.ResponseMessage);
        Assert.Equal(expectedResponse.HttpStatusCode, responseValue.HttpStatusCode);
        Assert.Null(responseValue.ResponseData);
    }

    [Fact]
    public async Task GetAllEvents_ErrorOccurred_ThrowsExceptionReturns500InternalServerError()
    {
        //Arrange
        var eventParams = GetWebhookEventParameters();
        var expectedResponse = GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(null, "An error occurred.", System.Net.HttpStatusCode.InternalServerError);
        _mockWebhookEventService.Setup(we => we.GetWebhookEventsAsync(eventParams, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("The operation was canceled."));

        //Act
        var result = await _controller.GetAllEvents(eventParams);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task GetAllEvents_CancellationTokenRequested_ThrowsExceptionReturns500InternalServerError()
    {
        //Arrange
        var eventParams = GetWebhookEventParameters();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var expectedResponse = GenericResponse<IReadOnlyList<WebhookEventDto>>.Failure(null, "An error occurred.", System.Net.HttpStatusCode.InternalServerError);
        _mockWebhookEventService.Setup(we => we.GetWebhookEventsAsync(eventParams, cts.Token))
            .ThrowsAsync(new OperationCanceledException("The operation was canceled."));

        //Act
        var result = await _controller.GetAllEvents(eventParams);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.Null(body.ResponseData);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task CreateEvent_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var createEntity = BuildCreateWebhookEventDto();
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "An error occurred.", HttpStatusCode.InternalServerError);

        _mockWebhookEventService.Setup(we => we.CreateEventAsync(createEntity, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _controller.CreateEvent(createEntity);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(expectedResponse.ResponseMessage, body.ResponseMessage);
    }

    [Fact]
    public async Task CreateEvent_CorrelationIdExists_Returns409Conflict()
    {
        //Arrange
        var createEntity = BuildCreateWebhookEventDto();
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "Correlation Id already exists.", HttpStatusCode.Conflict);
        _mockWebhookEventService.Setup(we => we.CreateEventAsync(createEntity, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);
        //Act
        var result = await _controller.CreateEvent(createEntity);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;
        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status409Conflict, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(expectedResponse.ResponseMessage, body.ResponseMessage);
    }

    [Fact]
    public async Task CreateEvent_InvalidEventType_Returns400BadRequest()
    {
        //Arrange
        var createEntity = BuildCreateWebhookEventDto(eventType: "InvalidEventType");
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "Invalid payload for event type.", HttpStatusCode.BadRequest);
        _mockWebhookEventService.Setup(we => we.CreateEventAsync(createEntity, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);
        //Act
        var result = await _controller.CreateEvent(createEntity);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;
        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(expectedResponse.ResponseMessage, body.ResponseMessage);
    }

    [Fact]
    public async Task CreateEvent_ValidEvent_Returns201Created()
    {
        //Arrange
        var createEntity = BuildCreateWebhookEventDto();
        var expectedResponse = GenericResponse<string>.Success("Webhook event created successfully.", "Webhook event created successfully.", HttpStatusCode.Created);
        _mockWebhookEventService.Setup(we => we.CreateEventAsync(createEntity, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);
        //Act
        var result = await _controller.CreateEvent(createEntity);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;
        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status201Created, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.Equal(expectedResponse.ResponseMessage, body.ResponseMessage);
    }

    [Fact]
    public async Task CreateEvent_InvalidPayload_Returns400BadRequest()
    {
        //Arrange
        var createEntity = BuildCreateWebhookEventDto(eventType: "CustomerCreated", payload: "InvalidPayload");
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "Invalid payload for event type.", HttpStatusCode.BadRequest);
        _mockWebhookEventService.Setup(we => we.CreateEventAsync(createEntity, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);
        //Act
        var result = await _controller.CreateEvent(createEntity);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;
        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(expectedResponse.ResponseMessage, body.ResponseMessage);
    }
}
