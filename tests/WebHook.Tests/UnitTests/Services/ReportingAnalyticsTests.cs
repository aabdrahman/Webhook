using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Core.Interfaces.Services;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class ReportingAnalyticsTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;
    private ServiceProvider _serviceProvider;

    public ReportingAnalyticsTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();
    }

    private Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock;

    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }

    private List<WebHookEventCatalog> _webHookEventCatalogs = [];
    private List<WebhookSubscription> _webhookSubscriptions = [];
    private List<WebhookEvent> _webhookEvents = [];

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddScoped<IReportingAnalyticsService, ReportingAnalyticsService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();


        await ctx.Database.EnsureDeletedAsync();
        //await ctx.Database.EnsureCreatedAsync();
        await ctx.Database.MigrateAsync();

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



        //await ctx.Database.ExecuteSqlRawAsync(@"
        //            CREATE OR REPLACE VIEW ""vw_GetMostRecentEvents"" AS
        //            SELECT ""we"".""CreatedAt"", ""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source"", COUNT(""wd"".""Id"") AS ""DeliveryCount""
        //            FROM ""WebhookDeliveries"" AS ""wd""
        //            INNER JOIN ""WebhookEvents"" AS ""we""
        //            ON ""wd"".""WebhookEventId"" = ""we"".""Id""
        //            GROUP BY ""wd"".""Id"",""we"".""CreatedAt"",""we"".""CorrelationId"", ""we"".""EventType"", ""we"".""Source""
        //            ORDER BY ""we"".""CreatedAt""
        //            LIMIT 5;");

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

    private ReportingAnalyticsService CreateSut(IAuthenticatedUserDetails authenticatedUserDetails = null)
    {
        var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var sut = new ReportingAnalyticsService(ctx, authenticatedUserDetails ?? _authenticatedUserDetailsMock.Object);
        return sut;
    }

    [Fact]
    public async Task GetMostRecentEventsAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetMostRecentEventsAsync(ct: cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred while fetching top five most recent events.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
    }

    [Fact]
    public async Task GetMostRecentEventsAsync_NoRecordedEvent_Returns200OK()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.GetMostRecentEventsAsync(CancellationToken.None);

        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Null(result.ErrorDetail);
        Assert.NotNull(result.ResponseData);
        Assert.Empty(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Most recent events fetched successfully.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetMostRecentEventsAsync_RecordedEvent_Returns200OKWithData()
    {
        //Arrange
        var sut = CreateSut();
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery1 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Processing, createdAt: DateTimeOffset.UtcNow.AddDays(-1), deliveredAt: DateTimeOffset.UtcNow.AddDays(-1));
        var delivery2 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Processing, createdAt: DateTimeOffset.UtcNow.AddDays(-2), deliveredAt: DateTimeOffset.UtcNow.AddDays(-2));
        var delivery3 = BuildDeliveryEntity(status: WebhookDeliveryStatus.Processing, createdAt: DateTimeOffset.UtcNow.AddDays(-3), deliveredAt: DateTimeOffset.UtcNow.AddDays(-3));
        await ctx.WebhookDeliveries.AddRangeAsync(delivery1, delivery2, delivery3);
        await ctx.SaveChangesAsync();
        //Act
        var result = await sut.GetMostRecentEventsAsync(CancellationToken.None);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Null(result.ErrorDetail);
        Assert.NotNull(result.ResponseData);
        Assert.NotEmpty(result.ResponseData);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Equal("Most recent events fetched successfully.", result.ResponseMessage, ignoreCase: true);
    }

    [Fact]
    public async Task GetTopActiveEventCatalogsAsync_CancellationRequested_Returns500INternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut();

        //Act
        var result = await sut.GetTopActiveEventCatalogsAsync(cts.Token);

        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred while fetching event catalogs analytics.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
    }

    [Fact]
    public async Task GetTopActiveEventCatalogsAsync_RecordExists_Returns200OK()
    {
        //Arrange
        var sut = CreateSut();

        //Act
        var result = await sut.GetTopActiveEventCatalogsAsync(CancellationToken.None);

        //Asseert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Null(result.ErrorDetail);
        Assert.NotNull(result.ResponseData);
    }

    [Fact]
    public async Task GetTopFailingSubscriptionsAsync_CancellationRequested_Returns500InternalServerError()
    {
        //Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateSut();
        //Act
        var result = await sut.GetTopFailingSubscriptionsAsync(cts.Token);
        //Assert
        Assert.NotNull(result);
        Assert.False(result.IsSuccessful);
        Assert.NotNull(result.ErrorDetail);
        Assert.Equal("An error occurred while fetching top failing subscriptions.", result.ResponseMessage, ignoreCase: true);
        Assert.Equal(HttpStatusCode.InternalServerError, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
    }

    [Fact]
    public async Task GetTopFailingSubscriptionsAsync_RecordExists_Returns200OK()
    {
        //Arrange
        var sut = CreateSut();
        //Act
        var result = await sut.GetTopFailingSubscriptionsAsync(CancellationToken.None);
        //Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.Null(result.ErrorDetail);
        Assert.NotNull(result.ResponseData);
    }
}
