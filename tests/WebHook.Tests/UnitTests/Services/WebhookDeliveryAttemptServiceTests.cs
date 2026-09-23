using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class WebhookDeliveryAttemptServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private ServiceProvider _serviceProvider;
    public WebhookDeliveryAttemptServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
    }
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
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddScoped<IWebhookDeliveryAttemptService, WebhookDeliveryAttemptService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        await ctx.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                ""WebhookDeliveryAttempts"",
                ""WebhookDeliveries"",
                ""WebhookEventSubscriptions"",
                ""WebhookSubscriptions"",
                ""WebhookEvents"",
                ""WebHookEventCatalogs""
            RESTART IDENTITY CASCADE;
        ");

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
                    !string.IsNullOrWhiteSpace(eventName) ? _webhookEvents.First(x => x.EventType.Equals(eventName, StringComparison.OrdinalIgnoreCase)) :
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

    //private WebhookDelivery BuildDeliveryEntity(WebhookDeliveryStatus status = WebhookDeliveryStatus.Pending, DateTimeOffset? nextRetryAt = null, DateTimeOffset? deliveredAt = null, int retryCount = 1, string? subscriptionName = null, string eventName = null) => new WebhookDelivery()
    //{
    //    Id = Guid.NewGuid(),
    //    NextRetryAt = nextRetryAt,
    //    DeliveredAt = deliveredAt,
    //    CallBackUrl = "https://example.com/",
    //    RetryCycle = 1,
    //    RetryCount = retryCount,
    //    RequestPayload = "{}",
    //    WebhookEventId = _webhookEvents.First(x => x.EventType.Equals(eventName ?? _webhookEvents.First().EventType, StringComparison.OrdinalIgnoreCase)).Id,
    //    WebhookSubscriptionEventId = _webhookSubscriptions.Where(x => x.Name.Equals(subscriptionName ?? _webhookSubscriptions.OrderBy(x => Guid.NewGuid()).First().Name, StringComparison.OrdinalIgnoreCase)).OrderBy(x => Guid.NewGuid()).SelectMany(x => x.WebhookEvents).First().Id
    //};

    private WebhookDeliveryAttempt BuildDeliveryAttemptEntity(Guid deliveryId, HttpStatusCode responseCode = HttpStatusCode.OK, string responseBody = "{}")
    {
        return new WebhookDeliveryAttempt()
        {
            WebhookDeliveryId = deliveryId,
            Id = Guid.NewGuid(),
            HttpResponse = responseBody,
            HttpResponseCode = responseCode.ToString(),
            AttemptedAt = DateTimeOffset.Now,
            Duration = 20.00,
            AttemptedCount = 1
        };
    }

    private WebhookDeliveryAttemptService CreateSut()
    {
        return new WebhookDeliveryAttemptService(_serviceProvider.GetRequiredService<RepositoryContext>());
    }

    [Fact]
    public async Task GetDeliveryAttemptsAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetDeliveryAttemptsAsync(Guid.NewGuid(), ct: cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Equal("An error occurrd while fetching delivery attempts.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetDeliveryAttemptsAsync_InvalidDeliveryId_Returns404NotFound()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.GetDeliveryAttemptsAsync(Guid.NewGuid(), CancellationToken.None);

        //Asert
        Assert.NotNull(result);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Webhook delivery attempts not yet made for the delievry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetDeliveryAttemptsAsync_DeliveryIdExistsWithAttempts_Returns200OK()
    {
        //Arrange
        Guid deliveryId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate = BuildDeliveryEntity();
            deliveryToCreate.WebhookDeliveryAttempts.Add(BuildDeliveryAttemptEntity(deliveryToCreate.Id));
            deliveryToCreate.WebhookDeliveryAttempts.Add(BuildDeliveryAttemptEntity(deliveryToCreate.Id));

            await arrangeCtx.AddAsync(deliveryToCreate);
            await arrangeCtx.SaveChangesAsync();

            deliveryId = deliveryToCreate.Id;
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetDeliveryAttemptsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Webhook delivery attempts fetched successfully.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(2, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetDeliveryAttemptsAsync_DeliveryIdExistsWithoutAttempts_Returns404NotFound()
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

        var sut = CreateSut();

        //Act
        var result = await sut.GetDeliveryAttemptsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Webhook delivery attempts not yet made for the delievry.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetDeliveryAttemptsAsync_DeliveryIdNotExistsWithoutAttempts_Returns404NotFound()
    {
        //Arrange
        Guid deliveryId = Guid.NewGuid();
        using (var scope = _serviceProvider.CreateScope())
        {
            var arrangeCtx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
            var deliveryToCreate = BuildDeliveryEntity();

            await arrangeCtx.AddAsync(deliveryToCreate);
            await arrangeCtx.SaveChangesAsync();
        }

        var sut = CreateSut();

        //Act
        var result = await sut.GetDeliveryAttemptsAsync(deliveryId, CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.Null(result.ResponseData);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Equal("Webhook delivery attempts not yet made for the delievry.", result.ResponseMessage, ignoreCase: true);
    }
}
