using Microsoft.EntityFrameworkCore;
using Moq;
using System.Net;
using WebHook.Core.DataTransferObjects.WebhookEvent;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookEventServiceTests
{
    private readonly DbContextOptions<RepositoryContext> _contextOptions;
    private readonly Mock<IApplicationPublisher> _applicationPublisherMock;
    public WebhookEventServiceTests()
    {
        _applicationPublisherMock = new Mock<IApplicationPublisher>();

        var builder = new DbContextOptionsBuilder<RepositoryContext>()
                            .UseInMemoryDatabase($"WebhookEventServiceTests_{Guid.NewGuid()}");

        _contextOptions = builder.Options;

        using var ctx = new RepositoryContext(_contextOptions);
        ctx.Database.EnsureCreated();
        List<WebHookEventCatalog> webhookEventCatalogs = new List<WebHookEventCatalog>()
        {
            BuildCatalogEntity(new List<string>() { "customerId", "customerName" }, "CustomerCreated"),
            BuildCatalogEntity(new List<string>() { "orderId", "orderAmount" }, "OrderPlaced"),
            BuildCatalogEntity(new List<string>() { "paymentId", "paymentStatus" }, "PaymentProcessed"),
            BuildCatalogEntity(new List<string>() { "shipmentId", "shipmentStatus" }, "ShipmentDispatched"),
        };
        SeedEventCatalog(ctx, webhookEventCatalogs);
    }

    private (RepositoryContext ctx, WebhookEventService svc) GetSut()
    {
        var context = new RepositoryContext(_contextOptions);
        var svc = new WebhookEventService(context, _applicationPublisherMock.Object);
        return (context, svc);  
    }

    private WebHookEventCatalog BuildCatalogEntity(List<string> availableFields, string name = "CustomerCreated") => new WebHookEventCatalog()
    {
        Id = Guid.NewGuid(),
        EventName = name,
        IsActive = true,
        Description = $"Test Event Catalog: {name}",
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = availableFields.ToDictionary(f => f, f => "string"),
        NormalizedEventName = name.ToUpper()
    };

    private CreateWebhookEventDto BuildCreateWebhookEventDto(string eventType = "CustomerCreated", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}",
                                                            string source = "TestSource", Guid? correlationId = null) => new CreateWebhookEventDto()
    {
        EventType = eventType,
        PayLoad = payload,
        Source = source,
        CorrelationId = correlationId ?? Guid.NewGuid()
    };

    private void SeedEventCatalog(RepositoryContext ctx, List<WebHookEventCatalog> webhookEventCatalogs)
    {
        ctx.WebHookEventCatalogs.AddRange(webhookEventCatalogs);
        ctx.SaveChanges();
    }

    private WebhookEvent BuildWebhookEventEntity(string eventType = "CustomerCreated", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}", 
                                                string source = "TestSource", Guid? correlationId = null) => new WebhookEvent()
    {
        Id = Guid.NewGuid(),
        EventType = eventType,
        PayLoad = payload,
        Source = source,
        CorrelationId = correlationId ?? Guid.NewGuid(),
        Status = WebHook.Core.Constants.WebHookEventStatus.Pending,
        CreatedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task CreateEventAsync_ShouldReturnFailure_WhenEventTypeNotInCatalog()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var createDto = BuildCreateWebhookEventDto(eventType: "NonExistentEvent");
        // Act
        var result = await svc.CreateEventAsync(createDto);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("Invalid event type.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
    }

    [Fact]
    public async Task CreateEventAsync_ShouldReturnSuccess_WhenValidInput()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var createDto = BuildCreateWebhookEventDto();

        // Act
        var result = await svc.CreateEventAsync(createDto);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal("Webhook event created successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.Created, result.HttpStatusCode);
        var createdEvent = await ctx.WebhookEvents.FirstOrDefaultAsync(e => e.CorrelationId == createDto.CorrelationId);
        Assert.NotNull(createdEvent);
        Assert.Equal(createDto.EventType.ToUpper(), createdEvent.EventType);
        Assert.Equal(createDto.PayLoad, createdEvent.PayLoad);
        Assert.Equal(createDto.Source, createdEvent.Source);
    }

    [Fact]
    public async Task CreateEventAsync_ShouldReturnFailure_WhenInvalidPayload()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var createDto = BuildCreateWebhookEventDto(payload: "{\"invalidField\":\"value\"}");
        // Act
        var result = await svc.CreateEventAsync(createDto);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.StartsWith("Invalid payload for event type.", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
    }

    [Fact]
    public async Task CreateEventAsync_ShouldReturnFailure_WhenCorrelationIdExists()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var existingEvent = BuildWebhookEventEntity();
        existingEvent.EventType = existingEvent.EventType.ToUpper(); // Ensure the event type is in uppercase to match the catalog
        ctx.WebhookEvents.Add(existingEvent);
        await ctx.SaveChangesAsync();
        var createDto = BuildCreateWebhookEventDto(correlationId: existingEvent.CorrelationId);

        // Act
        var result = await svc.CreateEventAsync(createDto);

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("Correlation Id already exists.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
    }

    [Fact]
    public async Task CreateEventAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var createDto = BuildCreateWebhookEventDto();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel the token immediately
        // Act
        var result = await svc.CreateEventAsync(createDto, cts.Token);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("An error occurred while creating the webhook event.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task CreateEventAsync_InvalidRequest_MissingFields_Returns400BadRequest()
    {
        //Arrange
        var sut = GetSut();
        var createDto = BuildCreateWebhookEventDto();
        createDto.PayLoad = "{\"custId\":\"12345\", \"customerName\":\"John Doe\"}"; // Simulate missing fields in payload
        _applicationPublisherMock.Setup(ap => ap.QueueEventRaised(It.IsAny<EventRaised>(), It.IsAny<CancellationToken>()));

        //Act
        var result = await sut.svc.CreateEventAsync(createDto);

        //Assert
        Assert.False(result.IsSuccessful);
        
        Assert.StartsWith("Invalid payload for event type.", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookEventAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var correlationId = Guid.NewGuid();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel the token immediately
        // Act
        var result = await svc.GetWebhookEventAsync(correlationId, cts.Token);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("An error occurred while fetching the webhook event.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookEventAsync_ShouldReturnFailure_WhenCorrelationIdNotFound()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var correlationId = Guid.NewGuid();
        // Act
        var result = await svc.GetWebhookEventAsync(correlationId);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("Webhook event not found.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookEventAsync_ShouldReturnSuccess_WhenCorrelationIdFound()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var existingEvent = BuildWebhookEventEntity();
        existingEvent.EventType = existingEvent.EventType.ToUpper(); // Ensure the event type is in uppercase to match the catalog
        ctx.WebhookEvents.Add(existingEvent);
        await ctx.SaveChangesAsync();
        // Act
        var result = await svc.GetWebhookEventAsync(existingEvent.CorrelationId);
        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal("Webhook event fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.ResponseData.Any());
        //var fetchedEvent = result.ResponseData.First();
        //Assert.Equal(existingEvent.Id, fetchedEvent.Id);
        //Assert.Equal(existingEvent.EventType, fetchedEvent.EventType);
        //Assert.Equal(existingEvent.PayLoad, fetchedEvent.PayLoad);
        //Assert.Equal(existingEvent.Source, fetchedEvent.Source);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_CancellationRequested_ThrowsException_Returns500InternalServerError()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var parameters = new GetWebhookEventParameters();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel the token immediately
        // Act
        var result = await svc.GetWebhookEventsAsync(parameters, cts.Token);
        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal("An error occurred while fetching the webhook events.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_ShouldReturnSuccess_WhenEventsExist()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var existingEvent1 = BuildWebhookEventEntity();
        var existingEvent2 = BuildWebhookEventEntity();
        existingEvent1.EventType = existingEvent1.EventType.ToUpper(); // Ensure the event type is in uppercase to match the catalog
        existingEvent2.EventType = existingEvent2.EventType.ToUpper(); // Ensure the event type is in uppercase to match the catalog
        ctx.WebhookEvents.AddRange(existingEvent1, existingEvent2);
        await ctx.SaveChangesAsync();
        var parameters = new GetWebhookEventParameters();
        // Act
        var result = await svc.GetWebhookEventsAsync(parameters);
        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal("Webhook events fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.True(result.ResponseData.Any());
    }

    [Fact]
    public async Task GetWebhookEventsAsync_ShouldReturnSuccess_WhenNoEventsExist()
    {
        // Arrange
        var (ctx, svc) = GetSut();
        var parameters = new GetWebhookEventParameters();
        // Act
        var result = await svc.GetWebhookEventsAsync(parameters);
        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal("Webhook events fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.False(result.ResponseData.Any());
    }

    [Fact]
    public async Task GetWebhookEventsAsync_Should_Filter_By_Source()
    {
        //Arrange
        var sut = GetSut();
        var entity1 = BuildWebhookEventEntity(source: "SourceA");
        var entity2 = BuildWebhookEventEntity(source: "SourceB");
        var entity3 = BuildWebhookEventEntity(source: "SourceA");

        await sut.ctx.AddAsync(entity1);
        await sut.ctx.AddAsync(entity2);
        await sut.ctx.AddAsync(entity3);
        await sut.ctx.SaveChangesAsync();

        //Act
        var parameters = new GetWebhookEventParameters { Source = "SourceA" };
        var result = await sut.svc.GetWebhookEventsAsync(parameters);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.False(!result.ResponseData.Any());
        Assert.Equal(2, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_Should_Filter_By_EventType()
    {
        //Arrange
        var sut = GetSut();
        var entity1 = BuildWebhookEventEntity(eventType: "CustomerCreated".ToUpper());
        var entity2 = BuildWebhookEventEntity(eventType: "OrderPlaced".ToUpper());
        var entity3 = BuildWebhookEventEntity(eventType: "CustomerCreated".ToUpper());
        await sut.ctx.AddAsync(entity1);
        await sut.ctx.AddAsync(entity2);
        await sut.ctx.AddAsync(entity3);
        await sut.ctx.SaveChangesAsync();
        //Act
        var parameters = new GetWebhookEventParameters { EventType = "CustomerCreated" };
        var result = await sut.svc.GetWebhookEventsAsync(parameters);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.False(!result.ResponseData.Any());
        Assert.Equal(2, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetWebhookEventsAsync_Should_Filter_By_Status()
    {
        //Arrange
        var sut = GetSut();
        var entity1 = BuildWebhookEventEntity();
        entity1.Status = WebHook.Core.Constants.WebHookEventStatus.Pending;
        var entity2 = BuildWebhookEventEntity();
        entity2.Status = WebHook.Core.Constants.WebHookEventStatus.Processed;
        var entity3 = BuildWebhookEventEntity();
        entity3.Status = WebHook.Core.Constants.WebHookEventStatus.Pending;
        await sut.ctx.AddAsync(entity1);
        await sut.ctx.AddAsync(entity2);
        await sut.ctx.AddAsync(entity3);
        await sut.ctx.SaveChangesAsync();
        //Act
        var parameters = new GetWebhookEventParameters { Status = WebHook.Core.Constants.WebHookEventStatus.Pending.ToString() };
        var result = await sut.svc.GetWebhookEventsAsync(parameters);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.False(!result.ResponseData.Any());
        Assert.Equal(2, result.ResponseData.Count);
    }

    public async Task GetWebhookEventsAsync_Should_Filter_By_CorrelationId()
    {
        //Arrange
        var sut = GetSut();
        var correlationId = Guid.NewGuid();
        var entity1 = BuildWebhookEventEntity(correlationId: correlationId);
        var entity2 = BuildWebhookEventEntity();
        var entity3 = BuildWebhookEventEntity(correlationId: correlationId);
        await sut.ctx.AddAsync(entity1);
        await sut.ctx.AddAsync(entity2);
        await sut.ctx.AddAsync(entity3);
        await sut.ctx.SaveChangesAsync();
        //Act
        var parameters = new GetWebhookEventParameters { CorrelationId = correlationId };
        var result = await sut.svc.GetWebhookEventsAsync(parameters);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.False(!result.ResponseData.Any());
        Assert.Equal(2, result.ResponseData.Count);
    }

    public async Task GetWebhookEventsAsync_Should_Filter_By_CreatedAtRange()
    {
        //Arrange
        var sut = GetSut();
        var now = DateTimeOffset.UtcNow;
        var entity1 = BuildWebhookEventEntity();
        entity1.CreatedAt = now.AddDays(-2);
        var entity2 = BuildWebhookEventEntity();
        entity2.CreatedAt = now.AddDays(-4);
        var entity3 = BuildWebhookEventEntity();
        entity3.CreatedAt = now.AddDays(-1);
        await sut.ctx.AddAsync(entity1);
        await sut.ctx.AddAsync(entity2);
        await sut.ctx.AddAsync(entity3);
        await sut.ctx.SaveChangesAsync();
        //Act
        var parameters = new GetWebhookEventParameters { CreatedAtFrom = now.AddDays(-3), CreatedAtTo = now };
        var result = await sut.svc.GetWebhookEventsAsync(parameters);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.False(!result.ResponseData.Any());
        Assert.Equal(2, result.ResponseData.Count);
    }
}
