using Microsoft.AspNetCore.Mvc;
using Moq;
using Serilog;
using System.Net;
using WebHook.Core.DataTransferObjects;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;
using WebHook.Core.Interfaces.Services;
using WebHook.Api.Controllers;
using Xunit;

namespace WebHook.UnitTests.Controllers;

/// <summary>
/// Unit tests for <see cref="WebhookDeadLetterQueueController"/>.
///
/// The service layer is fully mocked with Moq so these tests cover
/// only controller behaviour — status code mapping, response shaping,
/// and correct delegation to <see cref="IDeadLetterQueueService"/>.
///
/// No database or Testcontainers needed — the controller is thin and
/// contains no business logic of its own.
/// </summary>
public sealed class WebhookDeadLetterQueueControllerTests
{
    // -------------------------------------------------------------------------
    // Setup
    // -------------------------------------------------------------------------

    private readonly Mock<IDeadLetterQueueService> _serviceMock;
    private readonly WebhookDeadLetterQueueController _sut;

    public WebhookDeadLetterQueueControllerTests()
    {
        Log.Logger = new LoggerConfiguration().CreateLogger();

        _serviceMock = new Mock<IDeadLetterQueueService>();
        _sut         = new WebhookDeadLetterQueueController(_serviceMock.Object);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static DeadLetterQueueDto BuildDlqDto(Guid? id = null) => new(id: id ?? Guid.NewGuid(), createdAt: DateTimeOffset.UtcNow.AddHours(-1), reason: "Exceeded maximum retry attempts.", RetriedAt: null, RetryJustification: null, retriedBy: null);

    private static RequestManualRetryDto BuildRetryRequest(Guid? deadLetterId = null, Guid? deliveryId = null, string justification = "Endpoint is now healthy.") => new()
    {
        DeadLetterId       = deadLetterId ?? Guid.NewGuid(),
        RetryJustification = justification,
        DeliveryId = deliveryId ?? Guid.NewGuid()
    };

    // -------------------------------------------------------------------------
    // GetDeedLetterQueue — success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeedLetterQueue_ServiceReturnsItems_Returns200WithData()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();
        var items      = new List<DeadLetterQueueDto> { BuildDlqDto(), BuildDlqDto() };

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Success(items, "Dead letter queues fetched successfully.", HttpStatusCode.OK));

        // Act
        var result       = await _sut.GetDeedLetterQueue(deliveryId);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);

        var body = objectResult.Value as GenericResponse<IReadOnlyList<DeadLetterQueueDto>>;
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
        Assert.NotNull(body.ResponseData);
        Assert.Equal(2, body.ResponseData.Count);
    }

    [Fact]
    public async Task GetDeedLetterQueue_ServiceReturnsItems_CallsServiceWithCorrectDeliveryId()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Success(new List<DeadLetterQueueDto> { BuildDlqDto() }, "OK", HttpStatusCode.OK));

        // Act
        await _sut.GetDeedLetterQueue(deliveryId);

        // Assert — correct delivery ID forwarded to service
        _serviceMock.Verify(
            s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDeedLetterQueue_ServiceReturnsSingleItem_Returns200WithSingleItem()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();
        var item       = BuildDlqDto();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Success(new List<DeadLetterQueueDto> { item }, "OK", HttpStatusCode.OK));

        // Act
        var result       = await _sut.GetDeedLetterQueue(deliveryId);
        var objectResult = result as ObjectResult;
        var body         = objectResult!.Value as GenericResponse<IReadOnlyList<DeadLetterQueueDto>>;

        // Assert
        Assert.NotNull(body.ResponseData);
        Assert.Single(body!.ResponseData!);
        Assert.Equal(item.id, body.ResponseData.First().id);
    }

    // -------------------------------------------------------------------------
    // GetDeedLetterQueue — not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeedLetterQueue_NoItemsForDelivery_Returns404()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Failure(null, "Dead Letter queue items do not exist.", HttpStatusCode.NotFound));

        // Act
        var result       = await _sut.GetDeedLetterQueue(deliveryId);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.NotFound, objectResult.StatusCode);

        var body = objectResult.Value as GenericResponse<IReadOnlyList<DeadLetterQueueDto>>;
        Assert.NotNull(body);
        Assert.False(body.IsSuccessful);
        Assert.Null(body.ResponseData);
    }

    // -------------------------------------------------------------------------
    // GetDeedLetterQueue — server error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeedLetterQueue_ServiceReturnsServerError_Returns500()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Failure(null, "An unexpected error occurred.", HttpStatusCode.InternalServerError));

        // Act
        var result       = await _sut.GetDeedLetterQueue(deliveryId);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetDeedLetterQueue_ServiceThrowsException_Returns500WithoutPropagating()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result       = await _sut.GetDeedLetterQueue(deliveryId);
        var objectResult = result as ObjectResult;

        // Assert — exception caught, returns 500, does not propagate
        Assert.NotNull(objectResult);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetDeedLetterQueue_ServiceThrowsException_DoesNotThrow()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Fault."));

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => _sut.GetDeedLetterQueue(deliveryId));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // GetDeedLetterQueue — cancellation token forwarded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeedLetterQueue_CancellationTokenForwardedToService()
    {
        // Arrange
        var deliveryId = Guid.NewGuid();
        using var cts  = new CancellationTokenSource();

        _serviceMock
            .Setup(s => s.GetDeliveryDeadKetterAsync(deliveryId, cts.Token))
            .ReturnsAsync(GenericResponse<IReadOnlyList<DeadLetterQueueDto>>
                .Success(new List<DeadLetterQueueDto>(), "OK", HttpStatusCode.OK));

        // Act
        await _sut.GetDeedLetterQueue(deliveryId, cts.Token);

        // Assert — specific token forwarded
        _serviceMock.Verify(
            s => s.GetDeliveryDeadKetterAsync(deliveryId, cts.Token),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_ServiceReturnsSuccess_Returns200()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("Operation Successful.", "Manual retry requested successfully.", HttpStatusCode.OK));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.OK, objectResult.StatusCode);

        var body = objectResult.Value as GenericResponse<string>;
        Assert.NotNull(body);
        Assert.True(body.IsSuccessful);
    }

    [Fact]
    public async Task RequestManualRetry_ValidRequest_CallsServiceExactlyOnce()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "OK", HttpStatusCode.OK));

        // Act
        await _sut.RequestManualRetry(request);

        // Assert
        _serviceMock.Verify(
            s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_DeadLetterNotFound_Returns404()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Dead Letter with Id does not exist.", HttpStatusCode.NotFound));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.NotFound, objectResult.StatusCode);

        var body = objectResult.Value as GenericResponse<string>;
        Assert.False(body!.IsSuccessful);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — conflict (already retried)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_AlreadyRetried_Returns409Conflict()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Dead Letter queue already retried.", HttpStatusCode.Conflict));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.Conflict, objectResult.StatusCode);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — bad request (invalid delivery status)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_InvalidDeliveryStatus_Returns400()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Could not proceed. Delivery Status: Delivered", HttpStatusCode.BadRequest));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.BadRequest, objectResult.StatusCode);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — retry cycle exceeded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_RetryCycleExceeded_Returns422()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "Retry cycle already exceeded.", HttpStatusCode.UnprocessableEntity));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.UnprocessableEntity, objectResult.StatusCode);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — server error
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_ServiceReturnsServerError_Returns500()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Failure("Operation Failed.", "An unexpected error occurred.", HttpStatusCode.InternalServerError));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert
        Assert.NotNull(objectResult);
        Assert.Equal((int)HttpStatusCode.InternalServerError, objectResult.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_ServiceThrowsException_Returns500WithoutPropagating()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected fault."));

        // Act
        var result       = await _sut.RequestManualRetry(request);
        var objectResult = result as ObjectResult;

        // Assert — caught by controller catch block
        Assert.NotNull(objectResult);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task RequestManualRetry_ServiceThrowsException_DoesNotThrow()
    {
        // Arrange
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Fault."));

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => _sut.RequestManualRetry(request));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — cancellation token forwarded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_CancellationTokenForwardedToService()
    {
        // Arrange
        var request   = BuildRetryRequest();
        using var cts = new CancellationTokenSource();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, cts.Token))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "OK", HttpStatusCode.OK));

        // Act
        await _sut.RequestManualRetry(request, cts.Token);

        // Assert — specific token forwarded to service
        _serviceMock.Verify(
            s => s.RequestManualRetryAsync(request, cts.Token),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetry — logger missing on action (minor issue)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetry_MissingLoggerContext_StillCompletesSuccessfully()
    {
        // Arrange
        // Note: RequestManualRetry does not set _logger = _logger.ForContext(...)
        // before the try block unlike GetDeedLetterQueue. This is a minor
        // inconsistency but does not affect functionality.
        var request = BuildRetryRequest();

        _serviceMock
            .Setup(s => s.RequestManualRetryAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenericResponse<string>
                .Success("OK", "OK", HttpStatusCode.OK));

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => _sut.RequestManualRetry(request));

        Assert.Null(ex);
    }
}
