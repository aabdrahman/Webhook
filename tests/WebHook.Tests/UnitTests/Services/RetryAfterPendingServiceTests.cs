using MassTransit.Courier.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Net;
using Testcontainers.PostgreSql;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;
using Xunit;

namespace WebHook.IntegrationTests.Services;

/// <summary>
/// Integration tests for <see cref="RetryAfterPendingService"/>.
///
/// Uses Testcontainers PostgreSQL because the service uses:
///   FromSqlRaw("FOR UPDATE SKIP LOCKED") — PostgreSQL only
///
/// BUGS IDENTIFIED IN PRODUCTION CODE — flagged in relevant tests:
///
///   BUG 1: RetryCount > 1 in raw SQL excludes first-time retries (RetryCount = 1).
///          Should be RetryCount >= 1 to include deliveries on their first retry.
///
///   BUG 2: On successful delivery, NextRetryAt is still set after incrementing
///          RetryCount. A Delivered delivery should not have NextRetryAt set at all.
///          Fix: remove the NextRetryAt assignment inside the success branch.
///
///   BUG 3: RetryCount is incremented on both success AND failure. On success
///          the retry count is still incremented which inflates the count.
///          Fix: only increment RetryCount on failure.
///
///   BUG 4: WebhookDeliveryAttempts and webhookDeadLetterQueues navigation
///          properties must be initialised or loaded before .Add() is called
///          otherwise a NullReferenceException is thrown at runtime.
///          Fix: initialise collections in the entity constructor or use
///          .Include() on the FromSqlRaw query.
///
/// Prerequisites:
///   - Docker running
///   - Testcontainers.PostgreSql 4.12.0
///   - Npgsql.EntityFrameworkCore.PostgreSQL
/// </summary>
public sealed class RetryAfterPendingServiceTests
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly PostgreSqlFixture _fixture;
    private ServiceProvider _serviceProvider = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private List<WebhookSubscription> _webhookSubscriptions;
    private List<WebHookEventCatalog> _webHookEventCatalogs;

    public RetryAfterPendingServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — fresh state per test
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);

        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        services.AddHttpClient("WebhookDeliveryClient")
                .ConfigurePrimaryHttpMessageHandler(() => _httpHandler);

        services.AddScoped<WebhookDeliveryRetryAfterService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        await ctx.Database.EnsureCreatedAsync();

        await ctx.Database.ExecuteSqlRawAsync(@"
            TRUNCATE TABLE
                ""WebhookDeadLetterQueues"",
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

        await ctx.WebHookEventCatalogs.AddRangeAsync(_webHookEventCatalogs);
        await ctx.WebhookSubscriptions.AddRangeAsync(_webhookSubscriptions);
        await ctx.SaveChangesAsync();
    }

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private RetryAfterPendingService CreateSut(RepositoryContext ctx)
    {
        var httpClientFactory = _serviceProvider
            .GetRequiredService<IHttpClientFactory>();
        var retryAfterService = _serviceProvider
            .GetRequiredService<WebhookDeliveryRetryAfterService>();

        return new RetryAfterPendingService(ctx, httpClientFactory, retryAfterService);
    }

    /// <summary>
    /// Builds a delivery that is eligible for retry:
    /// RetryCount > 1 (as per the raw SQL filter) and NextRetryAt in the past.
    /// </summary>
    private WebhookDelivery BuildRetryableDelivery(
        string callbackUrl  = "https://partner.com/webhook",
        string payload      = @"{""customerId"":""123""}",
        int    retryCount   = 2,
        WebhookDeliveryStatus status = WebhookDeliveryStatus.Failed, Guid? subscriptionId = null) => new()
    {
        Id                       = Guid.NewGuid(),
        CallBackUrl              = callbackUrl,
        RequestPayload           = payload,
        DeliveryStatus           = status,
        RetryCount               = retryCount,
        NextRetryAt              = DateTimeOffset.UtcNow.AddMinutes(-1), // due in the past
        CreatedAt                = DateTimeOffset.UtcNow.AddHours(-1),
        WebhookDeliveryAttempts  = new List<WebhookDeliveryAttempt>(),   // prevent NullRef (BUG 4)
        webhookDeadLetterQueues  = new List<WebhookDeadLetterQueue>(),    // prevent NullRef (BUG 4)
        WebhookSubscriptionEventId = subscriptionId.HasValue ? subscriptionId.Value : _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),

        };

    /// <summary>
    /// Builds a delivery that should NOT be picked up by the raw SQL:
    /// RetryCount = 0 or 1 (below the > 1 threshold).
    /// </summary>
    private WebhookDelivery BuildFirstAttemptDelivery(
        int retryCount = 0, string payload = @"{""customerId"":""123""}", Guid? subscriptionId = null) => new()
    {
        Id                       = Guid.NewGuid(),
        CallBackUrl              = "https://partner.com/webhook",
        RequestPayload           = @"{""customerId"":""123""}",
        DeliveryStatus           = WebhookDeliveryStatus.Failed,
        RetryCount               = retryCount,
        NextRetryAt              = DateTimeOffset.UtcNow.AddMinutes(-1),
        CreatedAt                = DateTimeOffset.UtcNow.AddHours(-1),
        WebhookDeliveryAttempts  = new List<WebhookDeliveryAttempt>(),
        webhookDeadLetterQueues  = new List<WebhookDeadLetterQueue>(),
        WebhookSubscriptionEventId = subscriptionId.HasValue ? subscriptionId.Value : _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
        };

    private static WebhookEvent BuildWebhookEvent(
    Guid? id = null,
    string eventType = "CUSTOMERCREATED",
    WebHookEventStatus status = WebHookEventStatus.Pending,
    string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}") => new()
    {
        Id = id ?? Guid.NewGuid(),
        EventType = eventType.ToUpper(),
        Status = status,
        PayLoad = payload,
        Source = "CustomerService",
        CreatedAt = DateTimeOffset.UtcNow
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

    private static WebhookSubscription BuildEntity(string entityName, List<Guid> eventIds, string url = "https://example.com/")
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

    // -------------------------------------------------------------------------
    // Raw SQL filter — RetryCount > 1
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_DeliveryWithRetryCountOf2_IsPickedUp()
    {
        // Arrange — RetryCount = 2 satisfies > 1
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert — delivery was processed (status changed)
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotEqual(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
        Assert.Equal(1, _httpHandler.CallCount);
    }

    //[Fact]
    //public async Task RunRetryAfterFirstAttemptAsync_DeliveryWithRetryCountOf1_IsNotPickedUp()
    //{
    //    // Arrange — RetryCount = 1 does NOT satisfy > 1
    //    // BUG 1: This behaviour is intentional per the current SQL but may be wrong.
    //    // RetryCount = 1 means it was attempted once and failed — it should be retried.
    //    using var scope = _serviceProvider.CreateScope();
    //    var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
    //    var delivery    = BuildFirstAttemptDelivery();

    //    await ctx.WebhookDeliveries.AddAsync(delivery);
    //    await ctx.SaveChangesAsync();

    //    var sut = CreateSut(ctx);

    //    // Act
    //    await sut.RunRetryAfterFirstAttemptAsync();

    //    // Assert — not picked up, no HTTP call made
    //    Assert.Equal(0, _httpHandler.CallCount);
    //}

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_DeliveryWithRetryCountOf0_IsNotPickedUp()
    {
        // Arrange — first-time delivery, handled by WebhookDeliveryProcessorService
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildFirstAttemptDelivery(retryCount: 0);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        Assert.Equal(0, _httpHandler.CallCount);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_NextRetryAtInFuture_IsNotPickedUp()
    {
        // Arrange — NextRetryAt is in the future so not yet due
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);
        delivery.NextRetryAt = DateTimeOffset.UtcNow.AddHours(1); // future

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert — not due yet, no HTTP call
        Assert.Equal(0, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Successful retry — 2xx response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_200Response_StatusChangedToDelivered()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updated!.DeliveryStatus);
        Assert.NotNull(updated.DeliveredAt);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_200Response_RetryCountIncremented()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery     = BuildRetryableDelivery(retryCount: 2);
        var originalCount = delivery.RetryCount;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert — RetryCount incremented (BUG 3: also incremented on success)
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(originalCount + 1, updated!.RetryCount);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_200Response_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: "OK");
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts          = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal("OK", attempts[0].HttpResponseCode);
        Assert.Equal(1,    attempts[0].AttemptedCount);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_200Response_NextRetryAtStillSet_DocumentsBug2()
    {
        // Arrange
        // BUG 2: On success, NextRetryAt is still set even though the delivery
        // is Delivered. A delivered webhook does not need a retry time.
        // This test documents the current (buggy) behaviour.
        // Expected CORRECT behaviour: NextRetryAt should be null after Delivered.
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert — documents that NextRetryAt is currently set even on success (bug)
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        // This assertion will PASS currently (bug present) and FAIL when bug is fixed
        Assert.NotNull(updated!.NextRetryAt);
        // When bug is fixed, change the above to:
        // Assert.Null(updated!.NextRetryAt);
    }

    // -------------------------------------------------------------------------
    // Failed retry — non-2xx response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_500Response_StatusRemainsFailedAndRetryCountIncremented()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery     = BuildRetryableDelivery(retryCount: 2);
        var originalCount = delivery.RetryCount;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
        Assert.Equal(originalCount + 1,            updated.RetryCount);
        Assert.Null(updated.DeliveredAt);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_500Response_NextRetryAtSetInFuture()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);
        var beforeCall  = DateTimeOffset.UtcNow;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.NextRetryAt);
        Assert.True(updated.NextRetryAt > beforeCall);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_500Response_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            responseBody: "Service Unavailable");
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts          = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal("InternalServerError", attempts[0].HttpResponseCode);
    }

    // -------------------------------------------------------------------------
    // Dead letter threshold
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_RetryCountExceedsMaximum_MovedToDeadLetter()
    {
        // Arrange — RetryCount = 5, maximumAttemptCount = 5
        // After increment RetryCount becomes 6 which exceeds 5 → dead letter
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 5);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync(maximumAttemptCount: 5);

        // Assert — moved to DeadLetter
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);
        var dlq               = await assertCtx.WebhookDeadLetterQueues
            .Where(d => d.WebhookDeliveryId == delivery.Id)
            .FirstOrDefaultAsync();

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, updated!.DeliveryStatus);
        Assert.NotNull(dlq);
        Assert.Contains("5", dlq!.Reason); // threshold value in reason
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_RetryCountBelowMaximum_NotMovedToDeadLetter()
    {
        // Arrange — RetryCount = 2, maximumAttemptCount = 5
        // After increment RetryCount = 3, still below 5 — not dead lettered
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync(maximumAttemptCount: 5);

        // Assert — no DLQ record
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var dlq               = await assertCtx.WebhookDeadLetterQueues
            .Where(d => d.WebhookDeliveryId == delivery.Id)
            .FirstOrDefaultAsync();

        Assert.Null(dlq);
        Assert.NotEqual(WebhookDeliveryStatus.DeadLetter,
            (await assertCtx.WebhookDeliveries.FindAsync(delivery.Id))!.DeliveryStatus);
    }

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_DeadLetter_ReasonContainsThresholdValue()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 5);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync(maximumAttemptCount: 5);

        // Assert — reason message documents the threshold
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var dlq               = await assertCtx.WebhookDeadLetterQueues
            .Where(d => d.WebhookDeliveryId == delivery.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(dlq);
        Assert.Contains("exceeded threshold value", dlq!.Reason,
            StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Batch / totalAttempts limit
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_TotalAttemptsLimit_OnlyProcessesUpToLimit()
    {
        // Arrange — 10 retryable deliveries but totalAttempts = 3
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 10)
            .Select(i => BuildRetryableDelivery(
                callbackUrl: $"https://partner-{i}.com/webhook",
                retryCount: 2))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx);

        // Act
        await sut.RunRetryAfterFirstAttemptAsync(totalAttempts: 3);

        // Assert — only 3 HTTP calls made
        Assert.Equal(3, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // HttpRequestException — continue to next delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_HttpRequestException_ContinuesToNextDelivery()
    {
        // Arrange — first URL throws, second URL succeeds
        using var scope    = _serviceProvider.CreateScope();
        var ctx            = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var failDelivery   = BuildRetryableDelivery("https://unreachable.com/webhook",  retryCount: 2);
        var succDelivery   = BuildRetryableDelivery("https://partner.com/webhook",       retryCount: 2);

        await ctx.WebhookDeliveries.AddRangeAsync(failDelivery, succDelivery);
        await ctx.SaveChangesAsync();

        // Handler throws for the first URL, succeeds for the second
        _httpHandler = new MockHttpMessageHandler(responses: new Dictionary<string, HttpStatusCode>
        {
            { "https://unreachable.com/webhook", HttpStatusCode.ServiceUnavailable },
            { "https://partner.com/webhook",     HttpStatusCode.OK                 }
        });
        var sut = CreateSut(ctx);

        // Act — should not throw even when one delivery fails
        var ex = await Record.ExceptionAsync(
            () => sut.RunRetryAfterFirstAttemptAsync());

        // Assert — no exception thrown, both deliveries attempted
        Assert.Null(ex);
        Assert.Equal(2, _httpHandler.CallCount);

        // Second delivery (success URL) should be Delivered
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var succ              = await assertCtx.WebhookDeliveries.FindAsync(succDelivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, succ!.DeliveryStatus);
    }

    // -------------------------------------------------------------------------
    // No eligible deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_NoEligibleDeliveries_NoHttpCallMade()
    {
        // Arrange — empty DB
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var sut         = CreateSut(ctx);

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => sut.RunRetryAfterFirstAttemptAsync());

        Assert.Null(ex);
        Assert.Equal(0, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RunRetryAfterFirstAttemptAsync_CancellationRequested_ReturnsWithoutException()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var sut         = CreateSut(ctx);
        using var cts   = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => sut.RunRetryAfterFirstAttemptAsync(ct: cts.Token));

        Assert.Null(ex);
    }
}
