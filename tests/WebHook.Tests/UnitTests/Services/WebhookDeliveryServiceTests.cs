using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.WebhookDelivery;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookDeliveryServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    public WebhookDeliveryServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _authenticatedUserMock = new Mock<IAuthenticatedUserDetails>();
    }
    private Mock<IAuthenticatedUserDetails> _authenticatedUserMock;
    private ServiceProvider _serviceProvider;
    private List<WebHookEventCatalog> _webHookEventCatalogs = [];
    private List<WebhookSubscription> _webhookSubscriptions = [];
    private List<WebhookEvent> _webhookEvents = [];
    public async Task DisposeAsync()
    {
        if(_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString).EnableDetailedErrors();
        });

        
        services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        //await ctx.Database.ExecuteSqlRawAsync(@"
        //    TRUNCATE TABLE
        //        ""WebhookDeliveryAttempts"",
        //        ""WebhookDeliveries"",
        //        ""WebhookEventSubscriptions"",
        //        ""WebhookSubscriptions"",
        //        ""WebhookEvents"",
        //        ""WebHookEventCatalogs""
        //    RESTART IDENTITY CASCADE;
        //");

        _webHookEventCatalogs = new List<WebHookEventCatalog>()
        {
            BuildCatalogEntity(new List<string>() { "customerId", "customerName" }, "CustomerCreated"),
            BuildCatalogEntity(new List<string>() { "orderId", "orderAmount" }, "OrderPlaced"),
            BuildCatalogEntity(new List<string>() { "paymentId", "paymentStatus" }, "PaymentProcessed"),
            BuildCatalogEntity(new List<string>() { "shipmentId", "shipmentStatus" }, "ShipmentDispatched"),
        };

        _webhookSubscriptions = new List<WebhookSubscription>()
        {
            BuildEntity("Subscription A", eventIds: _webHookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList()),
            BuildEntity("Subscription B", eventIds: _webHookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList())
        };

        _webhookEvents = new List<WebhookEvent>()
        {
            BuildWebhookEventEntity(eventType: "UserCreated".ToUpper()),
            BuildWebhookEventEntity(eventType: "OrderCreated".ToUpper()),
            BuildWebhookEventEntity(eventType: "PaymentReceived".ToUpper())
        };

        await ctx.WebHookEventCatalogs.AddRangeAsync(_webHookEventCatalogs);
        await ctx.WebhookSubscriptions.AddRangeAsync(_webhookSubscriptions);
        await ctx.WebhookEvents.AddRangeAsync(_webhookEvents);
        await ctx.SaveChangesAsync();
    }

    //-----------------------------
    //UTILITY METHODS
    //-----------------------------
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

    private List<WebHookEventCatalog> GetEventCatalogs()
    {
        return
            [
               BuildCatalogEntity(["name", "email"]),
               BuildCatalogEntity(["productId", "orderCount"], "OrderCreated"),
               BuildCatalogEntity(["amount", "refrence"], "PaymentReceived"),
               BuildCatalogEntity(["name", "userid"], "UserCreated")
            ];
    }

    private WebhookSubscription BuildEntity(string entityName, List<Guid> eventIds)
    {
        var entityId = Guid.NewGuid();

        return new WebhookSubscription()
        {
            Id = entityId,
            Name = entityName,
            IsActive = true,
            SubscribedFields = [],
            CallbackUrl = "https://example.com/",
            SecretKey = Random.Shared.GetHexString(32),
            WebhookEvents = eventIds.Select(x => new WebhookSubscriptionEvent() { Id = Guid.NewGuid(), WebhookSubscriptionId = entityId, WebhookEventCatalogId = x }).ToList()
        };
    }

    private List<WebhookSubscription> GetSubscriptions()
    {
        return new List<WebhookSubscription>()
        {
            BuildEntity("user 1", _webHookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
            BuildEntity("user 2", _webHookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList()),
            BuildEntity("user 3", _webHookEventCatalogs.OrderBy(x => x.Id).Take(Random.Shared.Next(1, 4)).Select(x => x.Id).ToList())
        };
    }

    private WebhookEvent BuildWebhookEventEntity(string eventType = "CustomerCreated", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}", string source = "TestSource", Guid? correlationId = null)
    {
        return new WebhookEvent()
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            PayLoad = payload,
            Source = source,
            CorrelationId = correlationId ?? Guid.NewGuid(),
            Status = WebHook.Core.Constants.WebHookEventStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private WebhookDelivery BuildDeliveryEntity(WebhookDeliveryStatus status = WebhookDeliveryStatus.Pending, Guid? eventId = null, Guid? subscriptionId = null, 
        DateTimeOffset? createdAt = null, DateTimeOffset? nextRetryAt = null, DateTimeOffset? deliveredAt = null, 
        int retryCount = 1, string? subscriptionName = null, string? eventName = null)
    {
        var webhookEvent = eventId.HasValue ? _webhookEvents.First(x => x.Id == eventId.Value) : 
                    !string.IsNullOrWhiteSpace(eventName) ?  _webhookEvents.First(x => x.EventType.Equals(eventName, StringComparison.OrdinalIgnoreCase)) : 
                    _webhookEvents.OrderBy(x => x.Id).First();

        var subscription = subscriptionId.HasValue ? _webhookSubscriptions.First(x => x.Id == subscriptionId.Value) : !string.IsNullOrWhiteSpace(subscriptionName) ? 
                     _webhookSubscriptions.First(x => x.Name.Equals(subscriptionName, StringComparison.OrdinalIgnoreCase)) : 
                    _webhookSubscriptions.OrderBy(x => x.Id).First();

        return new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-2),
            NextRetryAt = nextRetryAt,
            DeliveredAt = deliveredAt,

            CallBackUrl = "https://example.com/",
            RetryCycle = 1,
            RetryCount = retryCount,

            RequestPayload = "{}",
            DeliveryStatus = status,

            WebhookEventId = webhookEvent.Id,

            WebhookSubscriptionEventId = subscription.WebhookEvents.First().Id
        };
    }

    //private WebhookDelivery BuildDeliveryEntity(WebhookDeliveryStatus status = WebhookDeliveryStatus.Pending, Guid? eventId = null, Guid? subscriptionId = null, DateTimeOffset? createdAt = null, 
    //    DateTimeOffset? nextRetryAt = null, DateTimeOffset? deliveredAt = null, int retryCount = 1, string? subscriptionName = null, string eventName = null)
    //{
    //    var desiredEventName = !string.IsNullOrEmpty(eventName) ? eventName : _webhookEvents.OrderBy(x => x.Id).First().EventType;
    //    var selectedEventId = _webhookEvents.First(x => x.EventType.Equals(desiredEventName)).Id;

    //    var subEventIdName = !string.IsNullOrEmpty(subscriptionName) ? subscriptionName : _webhookSubscriptions.OrderBy(x => x.Id).First().Name;
    //    var subEventId = _webhookSubscriptions.First(x => x.Name.Equals(subEventIdName, StringComparison.OrdinalIgnoreCase)).Id;
    //    return new WebhookDelivery()
    //    {
    //        Id = Guid.NewGuid(),
    //        CreatedAt = createdAt.HasValue ? createdAt.Value : DateTimeOffset.UtcNow.AddDays(-2),
    //        NextRetryAt = nextRetryAt,
    //        DeliveredAt = deliveredAt,
    //        CallBackUrl = "https://example.com/",
    //        RetryCycle = 1,
    //        RetryCount = retryCount,
    //        RequestPayload = "{}",
    //        DeliveryStatus = status,
    //        WebhookEventId = _webhookEvents.First(x => x.EventType.Equals(eventName ?? _webhookEvents.First().EventType, StringComparison.OrdinalIgnoreCase)).Id,
    //        WebhookSubscriptionEventId = _webhookSubscriptions.Where(x => x.Name.Equals(subscriptionName ?? _webhookSubscriptions.OrderBy(x => Guid.NewGuid()).First().Name, StringComparison.OrdinalIgnoreCase)).OrderBy(x => Guid.NewGuid()).SelectMany(x => x.WebhookEvents).First().Id
    //    };
    //}

    private WebhookDeliveryService CreateSut(IAuthenticatedUserDetails authenticatedUserDetails)
    {
        return new WebhookDeliveryService(_serviceProvider.GetRequiredService<RepositoryContext>(), authenticatedUserDetails);
    }

    [Fact]
    public async Task GetDeliveryDetailsAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());

        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveryDetailsAsync(Guid.NewGuid(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
    }

    [Fact]
    public async Task GetDeliveryDetailsAsync_DeliveryIdNotExists_Returns404NotFound()
    {
        //Arrange
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        Guid deliveryId = Guid.NewGuid();
        var sut = CreateSut(_authenticatedUserMock.Object);


        //Act
        var result = await sut.GetDeliveryDetailsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.StartsWith("Webhook delivery does not exst -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(deliveryId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeliveryDetailsAsync_DeliveryIdExists_Returns200OK()
    {
        //Arrange
        Guid deliveryId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate = BuildDeliveryEntity();

            await arrangeCtx.AddAsync(deliveryToCreate);
            await arrangeCtx.SaveChangesAsync();

            deliveryId = deliveryToCreate.Id;
        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveryDetailsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful, result.ResponseMessage);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Webhook delivery details fetched successfully.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetDeliveryDetailsAsync_InvalidDeliveryId_Returns404NotFound()
    {
        //Arrange

        Guid deliveryId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate = BuildDeliveryEntity(eventName: _webhookEvents.OrderBy(x => Guid.NewGuid()).First().EventType);

            await arrangeCtx.AddAsync(deliveryToCreate);
            await arrangeCtx.SaveChangesAsync();

        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveryDetailsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.StartsWith("Webhook delivery does not exst -", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(deliveryId.ToString(), result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDeliveriesAsync_CancellationRequested_Retunrs500INternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveriesAsync(new WebhookDeliveryQueryRequestParameter(), cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("An error occurred while fetching webhook deliveries.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetDeliveriesAsync_DeliveriesExistsWithDefaultFilter_Returns200OK()
    {
        //Arrange

        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate = BuildDeliveryEntity();

            await arrangeCtx.AddAsync(deliveryToCreate);
            await arrangeCtx.SaveChangesAsync();

        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveriesAsync(new WebhookDeliveryQueryRequestParameter(), CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData.metaData);
    }

    [Fact]
    public async Task GetDeliveriesAsync_DeliveriesExistsWithStatusFilter_Returns200OK()
    {
        //Arrange

        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate1 = BuildDeliveryEntity();
            var deliveryToCreate2 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Delivered, deliveredAt: DateTimeOffset.UtcNow.AddSeconds(-10));

            await arrangeCtx.AddRangeAsync(deliveryToCreate1, deliveryToCreate2);
            await arrangeCtx.SaveChangesAsync();

        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveriesAsync(new WebhookDeliveryQueryRequestParameter() { DeliveryStatus = WebhookDeliveryStatus.Pending }, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData.metaData);
        Assert.Equal(1, result.ResponseData.metaData.totalCount);
    }

    [Fact]
    public async Task GetDeliveriesAsync_DeliveriesExistsWithDateFilter_Returns200OK()
    {
        //Arrange

        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate1 = BuildDeliveryEntity();
            var deliveryToCreate2 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Delivered, deliveredAt: DateTimeOffset.UtcNow.AddSeconds(-5), createdAt: DateTimeOffset.UtcNow.AddDays(-5));

            await arrangeCtx.AddRangeAsync(deliveryToCreate1, deliveryToCreate2);
            await arrangeCtx.SaveChangesAsync();

        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveriesAsync(new WebhookDeliveryQueryRequestParameter() { StartDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)) }, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData.metaData);
        Assert.Equal(1, result.ResponseData.metaData.totalCount);
    }

    [Fact]
    public async Task GetDeliveriesAsync_DeliveriesExistsWithPageSize_Returns200OK()
    {
        //Arrange

        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate1 = BuildDeliveryEntity();
            var deliveryToCreate2 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Delivered, deliveredAt: DateTimeOffset.UtcNow.AddSeconds(-15), createdAt: DateTimeOffset.UtcNow.AddDays(-1));
            var deliveryToCreate3 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Failed);

            await arrangeCtx.AddRangeAsync(deliveryToCreate1, deliveryToCreate2, deliveryToCreate3);
            await arrangeCtx.SaveChangesAsync();

        }
        _authenticatedUserMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString());
        var sut = CreateSut(_authenticatedUserMock.Object);

        //Act
        var result = await sut.GetDeliveriesAsync(new WebhookDeliveryQueryRequestParameter() { PageSize = 1, PageNumber = 2 }, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful, result.ResponseMessage);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData.metaData);
        Assert.NotEqual(1, result.ResponseData.metaData.totalCount);
        Assert.True(result.ResponseData.metaData.hasNext);
        Assert.True(result.ResponseData.metaData.hasPrevious);
    }
}