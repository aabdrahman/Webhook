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
    private readonly Mock<IWebhookSubscriptionService> _webhookSubscriptionServiceMock;
    private readonly WebhookSubscriptionController _webhookSubscriptionController;
    public WebhookSubscriptionControllerTests()
    {
        _webhookSubscriptionServiceMock = new Mock<IWebhookSubscriptionService>();
        _webhookSubscriptionController = new WebhookSubscriptionController(_webhookSubscriptionServiceMock.Object);
    }

    private WebhookSubscriptionDto BuildSubscriptionDto(string name, List<string> subscribedEvents)
    {
        return new WebhookSubscriptionDto()
        {
            CreatedDate = DateTimeOffset.UtcNow,
            Id = Guid.NewGuid(),
            Name = name,
            SubscribedFields = [],
            SubscribedEvents = subscribedEvents,
            SecretKey = Random.Shared.GetHexString(12)
        };
    }

    private CreateWebhookSubscriptionDto BuildCreateSubscription(List<string> events)
    {
        return new CreateWebhookSubscriptionDto()
        {
            CallBackUrl = "https://example.com",
            SubscriberName = "Test 1",
            SubscribedEvents = events,
            SubscribedFields = ["name"]
        };
    }

    [Fact]
    public async Task GetAll_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookSubscriptionController.GetAll();
        var objRestult = Assert.IsType<ObjectResult>(result);
        var body = objRestult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objRestult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body.ErrorDetail);
    }

    [Fact]
    public async Task GetAll_NoSubscription_Returns404NotFound()
    {
        //Arrange
        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>.Failure(null, "No webhook subscription found.", HttpStatusCode.NotFound);
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookSubscriptionController.GetAll();
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetAll_SubscriptionExists_Returns200OK()
    {
        //Arrange

        var events = Enumerable.Range(1, 10).Select(x => Random.Shared.GetHexString(6)).ToList();

        var subscriptions = new List<WebhookSubscriptionDto>()
        {
            BuildSubscriptionDto("test 1", events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList()),
            BuildSubscriptionDto("test 2", events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList()),
            BuildSubscriptionDto("test 3", events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList())
        };

        var expectedResult = GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>.Success(subscriptions, "Subscription fetched successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock.Setup(ws => ws.GetAllWebhookSubscriptionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookSubscriptionController.GetAll();
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<IReadOnlyList<WebhookSubscriptionDto>>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.True(body.ResponseData.Any());
    }

    [Fact]
    public async Task GetById_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.GetWebhookSubscriptionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objRestult = Assert.IsType<ObjectResult>(result);
        var body = objRestult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, objRestult.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.NotNull(body.ErrorDetail);
    }

    [Fact]
    public async Task GetById_InvalidSubscriptionId_Returns404NotFound()
    {
        //Arrange
        var expectedResult = GenericResponse<WebhookSubscriptionDto>.Failure(null, "Subscription with id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock.Setup(ws => ws.GetWebhookSubscriptionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<WebhookSubscriptionDto>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    [Fact]
    public async Task GetById_SubscriptionExists_Returns200OK()
    {
        //Arrange

        var subscription = BuildSubscriptionDto("test 1", ["name", "email"]);

        var expectedResult = GenericResponse<WebhookSubscriptionDto>.Success(subscription, "Webhook subscription fetched successfully.", HttpStatusCode.OK);

        _webhookSubscriptionServiceMock.Setup(ws => ws.GetWebhookSubscriptionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookSubscriptionController.GetById(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<WebhookSubscriptionDto>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(body.ResponseData.Name, subscription.Name, ignoreCase: true);
    }

    [Fact]
    public async Task Delete_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.DeleteWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task Delete_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "Subscription id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock.Setup(ws => ws.DeleteWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);


        //Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task Delete_SubscriptionExists_Returns200OK()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Success("Operation Successful.", "Subscription deleted successfully.", HttpStatusCode.OK);
        _webhookSubscriptionServiceMock.Setup(ws => ws.DeleteWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionController.Delete(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        _webhookSubscriptionServiceMock.Setup(ws => ws.ActivateWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task ActivateSubscription_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "Subscription id does not exist.", HttpStatusCode.NotFound);

        _webhookSubscriptionServiceMock.Setup(ws => ws.ActivateWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);


        //Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status404NotFound, objResult.StatusCode);
        Assert.False(body.IsSuccessful);
    }

    [Fact]
    public async Task ActivateSubscription_SubscriptionExists_Returns200OK()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Success("Operation Successful.", "Subscription deleted successfully.", HttpStatusCode.OK);
        _webhookSubscriptionServiceMock.Setup(ws => ws.ActivateWebhookSubscriptionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionController.ActivateSubscription(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
    }

    [Fact]
    public async Task Create_ThrowsException_Returns500InternalServerError()
    {
        //Arrange
        var events = Enumerable.Range(1, 10).Select(x => Random.Shared.GetHexString(6)).ToList();

        var createdRequest = BuildCreateSubscription(events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());
        _webhookSubscriptionServiceMock.Setup(ws => ws.CreateWebhookSubscriptionAsync(It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());

        //Act
        var result = await _webhookSubscriptionController.Create(createdRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.NotNull(body.ErrorDetail);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status500InternalServerError, objResult.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidEvents_Returns400BadRequest()
    {
        //Arrange
        var events = Enumerable.Range(1, 10).Select(x => Random.Shared.GetHexString(6)).ToList();

        var createdRequest = BuildCreateSubscription(events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());
        createdRequest.SubscribedEvents.Add("tested");

        var expectedResult = GenericResponse<string>.Failure("Operation Failed.", "One or more events not found.", HttpStatusCode.BadRequest);

        _webhookSubscriptionServiceMock.Setup(ws => ws.CreateWebhookSubscriptionAsync(It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);

        //Act
        var result = await _webhookSubscriptionController.Create(createdRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Equal(StatusCodes.Status400BadRequest, objResult.StatusCode);

    }

    [Fact]
    public async Task Create_ValidRequest_Returns200OK()
    {
        //Arrange
        var expectedResponse = GenericResponse<string>.Success("Operation Successful.", "Subscription created successfully.", HttpStatusCode.OK);

        var events = Enumerable.Range(1, 10).Select(x => Random.Shared.GetHexString(6)).ToList();

        var createdRequest = BuildCreateSubscription(events.OrderBy(x => Guid.NewGuid()).Take(Random.Shared.Next(1, events.Count)).ToList());

        _webhookSubscriptionServiceMock.Setup(ws => ws.CreateWebhookSubscriptionAsync(It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

        //Act
        var result = await _webhookSubscriptionController.Create(createdRequest);
        var objResult = Assert.IsType<ObjectResult>(result);
        var body = objResult.Value as GenericResponse<string>;

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(body);
        Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
        Assert.True(body.IsSuccessful);
    }

    //[Fact]
    //public async Task Create_InvalidRequest_Returns400()
    //{
    //    //Arrange
    //    var invalidRequest = new CreateWebhookSubscriptionDto()
    //    {
    //        CallBackUrl = "http://example.com",
    //        SubscriberName = "Test 1",
    //        SubscribedEvents = [],
    //        SubscribedFields = ["name"]
    //    };

    //    var expectedResponse = GenericResponse<string>.Failure("Operation Failed.", "One or more events not found.", HttpStatusCode.BadRequest);

    //    _webhookSubscriptionServiceMock.Setup(ws => ws.CreateWebhookSubscriptionAsync(It.IsAny<CreateWebhookSubscriptionDto>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResponse);

    //    //Act
    //    var result = await _webhookSubscriptionController.Create(invalidRequest);
    //    var objResult = Assert.IsType<ObjectResult>(result);
    //    var body = objResult.Value as GenericResponse<string>;

    //    //Assert
    //    Assert.NotNull(result);
    //    Assert.NotNull(body);
    //    Assert.Equal(StatusCodes.Status200OK, objResult.StatusCode);
    //    Assert.True(body.IsSuccessful);
    //}
}
