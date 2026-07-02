using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Net;
using WebHook.Api.Controllers;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookEventCatalog;
using WebHook.Core.Interfaces.Services;

namespace WebHook.Tests.IntegrationTests;

public class WebhookEventCatalogControllerTests
{
    private readonly WebhookEventCatalogController _webhookEventCatalogController;
    private readonly Mock<IWebhookEventCatalogService> _webhookEventCatalogService;
    public WebhookEventCatalogControllerTests()
    {
        var _webhookEventCatalogServiceMock = new Mock<IWebhookEventCatalogService>();
        _webhookEventCatalogService = _webhookEventCatalogServiceMock;
        _webhookEventCatalogController = new WebhookEventCatalogController(_webhookEventCatalogServiceMock.Object);
    }

    private static EventCatalogDto GetSampleWebhookEventCatalogDto()
    {
        return new EventCatalogDto()
        {
            Id = Guid.NewGuid(),
            EventCatalogName = "CUSTOMERCREATED",
            Description = "Customer Created Description",
            IsActive = true,
            AvailableFields = { { "name", "string" }, { "email", "string" } }
        };
    }

    private static CreateEventCatalogDto GetSampleCreateEventCatalog()
    {
        return new CreateEventCatalogDto()
        {
            EventCatalogName = "OrderCreated",
            Description = "Order created description.",
            AvailableFields = { { "referenceNumber", "string" }, { "count", "int" } }
        };
    }


    [Fact]
    public async Task WebhookEventCatalogController_GetAllEventCatalog_Returns404NotFound()
    {
        //Arrange
        var expectedValue = GenericResponse<IReadOnlyList<EventCatalogDto>>.Failure(null, "No event catalog found.", HttpStatusCode.NotFound);
        _webhookEventCatalogService.Setup(wb => wb.GetAllEventCatalogAsync()).ReturnsAsync(expectedValue);

        //Act
        var result = await _webhookEventCatalogController.GetAllEventCatalog();

        //Assert
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status404NotFound);
        Assert.Same(objResult.Value, expectedValue);
    }

    [Fact]
    public async Task WebhookEventCatalogController_GetAllEventCatalog_ThrowsExceptionReturns500()
    {
        //Arange
        _webhookEventCatalogService.Setup(wb => wb.GetAllEventCatalogAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        IActionResult result = await _webhookEventCatalogController.GetAllEventCatalog();
        var objResult = Assert.IsType<ObjectResult>(result);

        //Assert
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
        Assert.Equal(objResult.Value, "An error occurred.");
    }

    [Fact]
    public async Task WebhookEventCatalogController_GetAllEventCatalog_Returns200WithData()
    {
        //Arrange
        var eventCatalogResponse = new List<EventCatalogDto>()
        {
            GetSampleWebhookEventCatalogDto(),
            GetSampleWebhookEventCatalogDto(),
            GetSampleWebhookEventCatalogDto()
        };
        var expectedResponse = GenericResponse<IReadOnlyList<EventCatalogDto>>.Success(eventCatalogResponse, "Events Fetched Successfully.", HttpStatusCode.OK);
        _webhookEventCatalogService.Setup(wb => wb.GetAllEventCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookEventCatalogController.GetAllEventCatalog();
        var objResult = result as ObjectResult;
        var body = objResult.Value as GenericResponse<IReadOnlyList<EventCatalogDto>>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal((int)objResult.StatusCode, (int)expectedResponse.HttpStatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(eventCatalogResponse.Count, body.ResponseData.Count);
    }

    [Fact]
    public async Task WebhookEventCatalogController_CreateEventCatalog_Returns201CreatedSuccessfully()
    {
        //Arrange
        var createEventCatalogRequest = GetSampleCreateEventCatalog();
        var expectedResponse = GenericResponse<string>.Success("Operation Successful", "Event Catalog Created SUccessfully.", HttpStatusCode.Created);
        _webhookEventCatalogService.Setup(wb => wb.CreateNewEventCatalogAsync(createEventCatalogRequest, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookEventCatalogController.CreateEventCatalog(createEventCatalogRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal((int)objResult.StatusCode, (int)expectedResponse.HttpStatusCode);  
    }

    [Fact]
    public async Task WebhookEventCatalogController_CreateEventCatalog_Returns409Conflict()
    {
        //Arrange
        var createEventCatalogRequest = GetSampleCreateEventCatalog();
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed", "Event Catalog Already Exists.", HttpStatusCode.Conflict);

        _webhookEventCatalogService.Setup(wb => wb.CreateNewEventCatalogAsync(createEventCatalogRequest, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookEventCatalogController.CreateEventCatalog(createEventCatalogRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body);
        Assert.Equal((int)objResult.StatusCode, (int)HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task WebhookEventCatalogController_CreateEventCatalog_Returns500InternalServerError()
    {
        //Arrange
        var createEventCatalogRequest = GetSampleCreateEventCatalog();
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed", "Event Catalog Already Exists.", HttpStatusCode.Conflict);

        _webhookEventCatalogService.Setup(wb => wb.CreateNewEventCatalogAsync(createEventCatalogRequest, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookEventCatalogController.CreateEventCatalog(createEventCatalogRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        //var body = objResult.Value as string;

        //Assert
        Assert.NotNull(result);
        //Assert.NotNull(body);
        Assert.Equal((int)objResult.StatusCode, (int)HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task WebhookEventCatalogController_GetEventCatalogById_Returns200EventCatalogExists()
    {
        //Arrange
        var eventCatalog = GetSampleWebhookEventCatalogDto();
        var expectedResponse = GenericResponse<EventCatalogDto>.Success(eventCatalog, "Event Catalog Fetched Successfully.", HttpStatusCode.OK);

        _webhookEventCatalogService.Setup(wb => wb.GetEventCatalogByIdAsync(eventCatalog.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookEventCatalogController.GetEventCatalogById(eventCatalog.Id);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<EventCatalogDto>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(objResult.StatusCode, (int)HttpStatusCode.OK);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
    }

    [Fact]
    public async Task WebhookEventCatalogController_GetEventCatalogById_Returns404EventCatalogDoesnotExist()
    {
        //Arrange
        Guid notExistId = Guid.NewGuid();
        var expectedResult = GenericResponse<EventCatalogDto>.Failure(null, "Event Catalog with Id does not exist.", HttpStatusCode.NotFound);
        _webhookEventCatalogService.Setup(wb => wb.GetEventCatalogByIdAsync(notExistId, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookEventCatalogController.GetEventCatalogById(notExistId);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<EventCatalogDto>;

        //Assert
        Assert.NotNull(result);
        Assert.Same(expectedResult, objResult.Value);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WebhookEventCatalogController_GetEventCatalogById_ThrowsExceptionReturns500InternalServerError()
    {
        //Arrange
        
        _webhookEventCatalogService.Setup(wb => wb.GetEventCatalogByIdAsync(Guid.NewGuid(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookEventCatalogController.GetEventCatalogById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);

        //Assert
        Assert.NotNull(result);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_True_Returns200SuccessfulOperation()
    {
        //Arrange
        Guid eventCatalogId = Guid.NewGuid();
        var expectedResult = GenericResponse<string>.Success("Operation Successful", "Eevent Catalog Operation Successful.", HttpStatusCode.OK);
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(eventCatalogId, true, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(eventCatalogId);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status200OK);
        Assert.Same(expectedResult, body);
        Assert.True(body.IsSuccessful);
    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_False_Returns200SuccessfulOperation()
    {
        //Arrange
        Guid eventCatalogId = Guid.NewGuid();
        bool activationAction = false;
        var expectedResult = GenericResponse<string>.Success("Operation Successful", "Event Catalog Operation Successful.", HttpStatusCode.OK);
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(eventCatalogId, activationAction, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(eventCatalogId, activationAction);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status200OK);
        Assert.Same(expectedResult, body);
        Assert.True(body.IsSuccessful);
    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_True_Returns404NotExistId()
    {
        //Arrange
        Guid notExistId = Guid.NewGuid();
        var expectedResult = GenericResponse<string>.Failure("Operation Failed.", "Event Catalog with Id does not exist.", HttpStatusCode.NotFound);
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(notExistId, true, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(notExistId);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.False(body.IsSuccessful);
        Assert.Same(expectedResult, body);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status404NotFound);
    
    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_False_Returns404NotExistId()
    {
        //Arrange
        Guid notExistId = Guid.NewGuid();
        var expectedResult = GenericResponse<string>.Failure("Operation Failed.", "Event Catalog with Id does not exist.", HttpStatusCode.NotFound);
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(notExistId, false, It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(notExistId, false);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.False(body.IsSuccessful);
        Assert.Same(expectedResult, body);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status404NotFound);

    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_True_ThrowsExceptionReturns500InternalServerError()
    {
        //Arrange
        Guid eventCatalogId = Guid.NewGuid();
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(eventCatalogId, true, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(eventCatalogId, true);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.False(body.IsSuccessful);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task WebhookEventCatalogController_ActivationAction_False_ThrowsExceptionReturns500InternalServerError()
    {
        //Arrange
        Guid eventCatalogId = Guid.NewGuid();
        _webhookEventCatalogService.Setup(wb => wb.EventCatalogActivationAsync(eventCatalogId, false, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookEventCatalogController.ActivationAction(eventCatalogId, false);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.False(body.IsSuccessful);
        Assert.Equal(objResult.StatusCode, StatusCodes.Status500InternalServerError);
    }
}
