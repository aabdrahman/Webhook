using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Net;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookSubscriptionEventServiceTests : IDisposable
{
    private readonly DbContextOptions<RepositoryContext> _repositoryDbContextOptions;
    

    public WebhookSubscriptionEventServiceTests()
    {
        var dbName = $"WebhookSubscriptionEventServiceTests_{Guid.NewGuid()}";

        var dbContextBuilder = new DbContextOptionsBuilder<RepositoryContext>().EnableSensitiveDataLogging()
                                    .UseInMemoryDatabase(dbName);

        _repositoryDbContextOptions = dbContextBuilder.Options;

        using var ctx = new RepositoryContext(_repositoryDbContextOptions);
        ctx.Database.EnsureCreated();
        
        ctx.WebHookEventCatalogs.AddRange(webhookEventCatalogs);
        ctx.SaveChanges();
    }

    private (WebhookSubscriptionEventService svc, RepositoryContext ctx) GetSut()
    {
        var ctx = new RepositoryContext(_repositoryDbContextOptions);
        return (new WebhookSubscriptionEventService(ctx), ctx);
    }

    private WebhookSubscription BuildEntity(string entityName, List<Guid> eventIds, string url = "https://example.com/")
    {
        var entityId = Guid.NewGuid();

        return new WebhookSubscription()
        {
            Id = entityId,
            Name = entityName,
            IsActive = true,
            SubscribedFields = [],
            CallbackUrl = url,
            SecretKey = Random.Shared.GetHexString(32),
            WebhookEvents = eventIds.Select(x => new WebhookSubscriptionEvent() { WebhookSubscriptionId = entityId, WebhookEventCatalogId = x, CreatedAt = DateTimeOffset.UtcNow, IsActive = true }).ToList()
        };
    }

    private static List<WebHookEventCatalog> webhookEventCatalogs = new List<WebHookEventCatalog>()
    {
        BuildCatalogEntity(new List<string>() { "customerId", "customerName" }, "CustomerCreated"),
        BuildCatalogEntity(new List<string>() { "orderId", "orderAmount" }, "OrderPlaced"),
        BuildCatalogEntity(new List<string>() { "paymentId", "paymentStatus" }, "PaymentProcessed"),
        BuildCatalogEntity(new List<string>() { "shipmentId", "shipmentStatus" }, "ShipmentDispatched"),
    };

    private static WebHookEventCatalog BuildCatalogEntity(List<string> availableFields, string name = "CustomerCreated") => new WebHookEventCatalog()
    {
        Id = Guid.NewGuid(),
        EventName = name,
        IsActive = true,
        Description = $"Test Event Catalog: {name}",
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = availableFields.ToDictionary(f => f, f => "string"),
        NormalizedEventName = name.ToUpper()
    };

    public void Dispose()
    {
        using var ctx = new RepositoryContext(_repositoryDbContextOptions);
        ctx.Database.EnsureDeleted();
    }

    [Fact]
    public async Task GetSubscribedEventsAsync_ThrowsException_CancellationTokenRequested()
    {
        //Arrange
        var sut = GetSut();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.GetSubscribedEventsAsync(Guid.NewGuid(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(StatusCodes.Status500InternalServerError, (int)result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task GetSubscribedEventsAsync_SubscriptionIdNotExists_Returns404NotFound()
    {
        //Arrange

        var sut = GetSut();


        //Act
        var result = await sut.svc.GetSubscribedEventsAsync(Guid.NewGuid());

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetSubscribedEventsAsync_SubscriptionIdAndSubscribedEventExists_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var webhookEventCatalogCount = webhookEventCatalogs.Count;
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: webhookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogCount)).ToList());

        await sut.ctx.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();
        //Act
        var result = await sut.svc.GetSubscribedEventsAsync(subscriptionEntity.Id);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal(await sut.ctx.WebhookEventSubscriptions.CountAsync(x => x.WebhookSubscriptionId == subscriptionEntity.Id), result.ResponseData.Count);
    }

    [Fact]
    public async Task GetSubscribedEventsAsync_SubscriptionIdExistsEventDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();
        var webhookEventCatalogCount = webhookEventCatalogs.Count;
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: webhookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogCount)).ToList());
        subscriptionEntity.WebhookEvents = [];

        await sut.ctx.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.GetSubscribedEventsAsync(subscriptionEntity.Id);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task UnsubscribeFromEventAsync_ThrowsException_CancellationTokenRequested()
    {
        //Arrange
        var sut = GetSut();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.UnsubscribeFromEventAsync(Guid.NewGuid(), "test" , cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(StatusCodes.Status500InternalServerError, (int)result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task UnsubscribeFromEventAsync_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();
        var eventName = webhookEventCatalogs.Select(x => x.NormalizedEventName).First();

        //Act
        var result = await sut.svc.UnsubscribeFromEventAsync(Guid.NewGuid(), eventName);

        //Assert
        Assert.NotNull(result);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task UnsubscribeFromEventAsync_SubscriptionIdExistsEventNotExists()
    {
        //Arrange
        var sut = GetSut();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: webhookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).Take(Random.Shared.Next(1, 2)).ToList());
        

        //Act
        var result = await sut.svc.UnsubscribeFromEventAsync(subscriptionEntity.Id, "test");

        //Assert
        Assert.NotNull(result);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task UnsubscribeFromEventAsync_ValidRequest_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var eventsToAdd = webhookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogs.Count)).ToList();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: eventsToAdd.Select(x => x.Id).ToList());

        var eventToRemove = eventsToAdd.Select(x => x.EventName).First();

        await sut.ctx.WebhookSubscriptions.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.UnsubscribeFromEventAsync(subscriptionEntity.Id, eventToRemove);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Null(result.ErrorDetail);

        var deactivatedEvent = await sut.ctx.WebhookEventSubscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.WebhookSubscriptionId == subscriptionEntity.Id && x.webHookEventCatalog.NormalizedEventName == eventToRemove.ToUpper());
        Assert.NotNull(deactivatedEvent);
        Assert.False(deactivatedEvent.IsActive);
    }

    [Fact]
    public async Task SubscribeToEventAsync_ThrowsException_CancellationTokenRequested()
    {
        //Arrange
        var sut = GetSut();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        //Act
        var result = await sut.svc.SubscribeToEventAsync(Guid.NewGuid(), "test", cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(StatusCodes.Status500InternalServerError, (int)result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task SubscribeToEventAsync_SubscriptionIdDoesNotExist_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();

        //Act
        var result = await sut.svc.SubscribeToEventAsync(Guid.NewGuid(), "CustomerCreated");

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task SubscribeToEventAsync_SubscriptionExistsEventNotExists_Returns404NotFound()
    {
        //Arrange
        var sut = GetSut();
        var eventsToAdd = webhookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogs.Count)).ToList();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: eventsToAdd.Select(x => x.Id).ToList());

        var eventToRemove = eventsToAdd.Select(x => x.EventName).First();

        await sut.ctx.WebhookSubscriptions.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.SubscribeToEventAsync(subscriptionEntity.Id, "InvalidEvent");

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task SubscribeToEventAsync_EventSubscribed_Returns409Conflict()
    {
        //Arrange
        var sut = GetSut();
        var eventsToAdd = webhookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogs.Count)).ToList();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: eventsToAdd.Select(x => x.Id).ToList());

        var eventToResubscribe = eventsToAdd.Select(x => x.EventName).First();

        await sut.ctx.WebhookSubscriptions.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.SubscribeToEventAsync(subscriptionEntity.Id, eventToResubscribe);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
        Assert.NotNull(result.ErrorDetail);
    }

    [Fact]
    public async Task SubscribeToEventAsync_DeactivatedEvent_Reativated_Returns200OK()
    {
        //Arrange
        var sut = GetSut();
        var eventsToAdd = webhookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogs.Count)).ToList();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: eventsToAdd.Select(x => x.Id).ToList());

        var eventToDeactivate = eventsToAdd.First();
        subscriptionEntity.WebhookEvents.Where(x => x.Id == eventToDeactivate.Id).ToList().ForEach(x => x.IsActive = true);


        await sut.ctx.WebhookSubscriptions.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var deactivateResult = await sut.svc.UnsubscribeFromEventAsync(subscriptionEntity.Id, eventToDeactivate.EventName);
        var result = await sut.svc.SubscribeToEventAsync(subscriptionEntity.Id, eventToDeactivate.EventName);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);

        var eventReactivated = await sut.ctx.WebhookEventSubscriptions.FirstOrDefaultAsync(x => x.WebhookSubscriptionId == subscriptionEntity.Id && x.webHookEventCatalog.NormalizedEventName == eventToDeactivate.EventName.ToUpper());
        Assert.NotNull(eventReactivated);
        Assert.True(eventReactivated.IsActive);
    }

    [Fact]
    public async Task SubscribeToEventAsync_ValidRequest_Returns200OK()
    {
        //Arrange

        var sut = GetSut();
        var eventsToAdd = webhookEventCatalogs.Where(x => !x.EventName.Contains("CustomerCreated", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Id).Take(Random.Shared.Next(1, webhookEventCatalogs.Count-1)).ToList();
        var subscriptionEntity = BuildEntity("Test Entity", eventIds: eventsToAdd.Select(x => x.Id).ToList());

        await sut.ctx.WebhookSubscriptions.AddAsync(subscriptionEntity);
        await sut.ctx.SaveChangesAsync();

        //Act
        var result = await sut.svc.SubscribeToEventAsync(subscriptionEntity.Id, "CustomerCreated");

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);

        var newlyInserted = await sut.ctx.WebhookEventSubscriptions.Include(x => x.webHookEventCatalog).FirstOrDefaultAsync(x => x.WebhookSubscriptionId == subscriptionEntity.Id && x.webHookEventCatalog.NormalizedEventName == "CustomerCreated".ToUpper());
        Assert.NotNull(newlyInserted);
        Assert.True(newlyInserted.IsActive);
        Assert.Equal("CustomerCreated", newlyInserted.webHookEventCatalog.NormalizedEventName, ignoreCase: true);
    }
}
