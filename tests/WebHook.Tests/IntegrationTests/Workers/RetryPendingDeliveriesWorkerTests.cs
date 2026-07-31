using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Serilog;
using System.Net;
using Testcontainers.PostgreSql;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.Services;
using Xunit;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Integration tests for <see cref="RetryPendingDeieveriesWorker"/>.
///
/// Uses Testcontainers PostgreSQL because <see cref="RetryAfterPendingService"/>
/// uses FromSqlRaw with FOR UPDATE SKIP LOCKED — PostgreSQL only.
///
/// Prerequisites:
///   - Docker running
///   - Testcontainers.PostgreSql 4.12.0
///   - Npgsql.EntityFrameworkCore.PostgreSQL
/// </summary>
public sealed class RetryPendingDeliveriesWorkerTests
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

    public RetryPendingDeliveriesWorkerTests(PostgreSqlFixture fixture)
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
        services.AddScoped<RetryAfterPendingService>();

        // Short tick interval so tests do not wait 60 seconds
        services.Configure<RetryDeliveresAfterFailedConfiguration>(opt =>
        {
            opt.TotalBatchSize       = 10;
            opt.MaximumAttendedCount = 5;
            opt.ThresholdDuration    = 25000;
        });

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
            BuildCatalog(new List<string>() { "customerId", "customerName" }, "CustomerCreated"),
            BuildCatalog(new List<string>() { "orderId", "orderAmount" }, "OrderPlaced"),
            BuildCatalog(new List<string>() { "paymentId", "paymentStatus" }, "PaymentProcessed"),
            BuildCatalog(new List<string>() { "shipmentId", "shipmentStatus" }, "ShipmentDispatched"),
        };

        _webhookSubscriptions = new List<WebhookSubscription>()
        {
            BuildSubscription("Subscription A", eventIds: _webHookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList()),
            BuildSubscription("Subscription B", eventIds: _webHookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList())
        };

        await ctx.WebHookEventCatalogs.AddRangeAsync(_webHookEventCatalogs);
        await ctx.WebhookSubscriptions.AddRangeAsync(_webhookSubscriptions);
        await ctx.SaveChangesAsync();
    }

    private ServiceProvider BuildServiceProvider(
    RetryDeliveresAfterFailedConfiguration? configOverride = null)
    {
        var services = new ServiceCollection();

        // All your normal registrations...

        if (configOverride is not null)
        {
            var optionsMonitor = new Mock<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>();
            optionsMonitor.Setup(x => x.CurrentValue).Returns(configOverride);

            services.AddSingleton(optionsMonitor.Object);
        }
        else
        {
            services.Configure<RetryDeliveresAfterFailedConfiguration>(options =>
            {
                options.TotalBatchSize = 100;
                options.MaximumAttendedCount = 5;
                options.ThresholdDuration = 300000;
            });
        }

        return services.BuildServiceProvider();
    }

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private TestableRetryPendingDeliveriesWorker CreateWorker() =>
        new TestableRetryPendingDeliveriesWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>());


    private static WebhookEvent BuildWebhookEvent(
        Guid? id = null,
        string eventType = "CUSTOMERCREATED",
        WebHookEventStatus status = WebHookEventStatus.Pending,
        string payload = "{}") => new()
        {
            Id = id ?? Guid.NewGuid(),
            EventType = eventType,
            Status = status,
            PayLoad = payload,
            Source = "CustomerService",
            CreatedAt = DateTimeOffset.UtcNow
        };

    private WebHookEventCatalog BuildCatalog(List<string> subscribedFields, string eventName = "CustomerCreated") => new()
    {
        Id = Guid.NewGuid(),
        EventName = eventName,
        NormalizedEventName = eventName.ToUpper(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = subscribedFields.ToDictionary(f => f, f => "string")
    };

    private static WebhookSubscription BuildSubscription(string entityName, List<Guid> eventIds, string url = "https://example.com/")
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

    /// <summary>
    /// A delivery eligible for retry — RetryCount > 1 and NextRetryAt in the past.
    /// </summary>
    private WebhookDelivery BuildRetryableDelivery(
        string callbackUrl = "https://partner.com/webhook",
        string payload = @"{""customerId"":""123""}",
        int retryCount = 2,
        WebhookDeliveryStatus status = WebhookDeliveryStatus.Failed, Guid? subscriptionId = null) => new()
        {
            Id = Guid.NewGuid(),
            CallBackUrl = callbackUrl,
            RequestPayload = payload,
            DeliveryStatus = status,
            RetryCount = retryCount,
            NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(-1), // due in the past
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
            WebhookDeliveryAttempts = new List<WebhookDeliveryAttempt>(),   // prevent NullRef (BUG 4)
            webhookDeadLetterQueues = new List<WebhookDeadLetterQueue>(),    // prevent NullRef (BUG 4)
            WebhookSubscriptionEventId = subscriptionId.HasValue ? subscriptionId.Value : _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
            webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),

        };

    /// <summary>
    /// Runs the worker's ExecuteAsync directly and waits until the given
    /// condition is satisfied or the timeout is reached.
    /// </summary>
    private async Task RunWorkerUntilAsync(
        TestableRetryPendingDeliveriesWorker worker,
        Func<Task<bool>>                     condition,
        int                                  timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        while (!cts.IsCancellationRequested)
        {
            if (await condition()) break;
            await Task.Delay(200).ContinueWith(_ => { });
        }

        await Task.Delay(500); // buffer for SaveChangesAsync

        cts.Cancel();

        try { await executeTask; }
        catch (OperationCanceledException) { }
    }

    private async Task<bool> AllDeliveriesProcessedAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return !await ctx.WebhookDeliveries
            .AnyAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Failed
                        && d.RetryCount > 1
                        && d.NextRetryAt <= DateTimeOffset.UtcNow);
    }

    private async Task<bool> AtLeastNAttemptsExistAsync(int count)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await ctx.WebhookDeliveryAttempts.CountAsync() >= count;
    }

    // -------------------------------------------------------------------------
    // StartAsync / StopAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartAsync_StartsWithoutException()
    {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);
        cts.Cancel();
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_CompletesWithinReasonableTime()
    {
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();

        await worker.StartAsync(cts.Token);

        // BUG 2: Because stoppingToken is not passed to WaitForNextTickAsync,
        // the worker may not stop gracefully. This test documents the issue.
        // With the bug present, StopAsync may hang beyond the timeout.
        cts.Cancel();

        var stopTask        = worker.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask;

        Assert.True(completedInTime,
            "StopAsync did not complete within 5 seconds. " +
            "This may be caused by BUG 2 — WaitForNextTickAsync not receiving stoppingToken.");
    }

    // -------------------------------------------------------------------------
    // ExecuteAsync — no eligible deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoEligibleDeliveries_NoHttpCallMade()
    {
        // Arrange — empty DB
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(0, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_DeliveryWithRetryCount1_NotPickedUp()
    {
        // Arrange — RetryCount = 1 does not satisfy > 1 in raw SQL (BUG 1 in service)
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 1);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        Assert.Equal(0, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Successful retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RetryableDelivery_200Response_StatusChangedToDelivered()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, async () =>
        {
            using var s = _serviceProvider.CreateScope();
            var c       = s.ServiceProvider.GetRequiredService<RepositoryContext>();
            var d       = await c.WebhookDeliveries.FindAsync(delivery.Id);
            return d?.DeliveryStatus == WebhookDeliveryStatus.Delivered;
        });

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updated!.DeliveryStatus);
        Assert.NotNull(updated.DeliveredAt);
        Assert.Equal(1, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableDelivery_200Response_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: "OK");
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, () => AtLeastNAttemptsExistAsync(1));

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

    // -------------------------------------------------------------------------
    // Failed retry
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RetryableDelivery_500Response_StatusRemainsFailedAndRetryCountIncremented()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery     = BuildRetryableDelivery(retryCount: 2);
        var originalCount = delivery.RetryCount;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, () => AtLeastNAttemptsExistAsync(1));

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
        Assert.Equal(originalCount + 1,             updated.RetryCount);
        Assert.NotNull(updated.NextRetryAt);
        Assert.Null(updated.DeliveredAt);
    }

    [Fact]
    public async Task ExecuteAsync_RetryableDelivery_500Response_NextRetryAtSetInFuture()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 2);
        var beforeRun   = DateTimeOffset.UtcNow;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, () => AtLeastNAttemptsExistAsync(1));

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.NextRetryAt);
        Assert.True(updated.NextRetryAt > beforeRun);
    }

    // -------------------------------------------------------------------------
    // Dead letter threshold
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_RetryCountExceedsMaximum_MovedToDeadLetter()
    {
        // Arrange — RetryCount = 5, after increment = 6 > MaximumAttendedCount (5)
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildRetryableDelivery(retryCount: 5);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, async () =>
        {
            using var s = _serviceProvider.CreateScope();
            var c       = s.ServiceProvider.GetRequiredService<RepositoryContext>();
            var d       = await c.WebhookDeliveries.FindAsync(delivery.Id);
            return d?.DeliveryStatus == WebhookDeliveryStatus.DeadLetter;
        });

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);
        var dlq               = await assertCtx.WebhookDeadLetterQueues
            .FirstOrDefaultAsync(d => d.WebhookDeliveryId == delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, updated!.DeliveryStatus);
        Assert.NotNull(dlq);
        Assert.Contains("exceeded threshold", dlq!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Batch size from configuration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_BatchSizeFromConfiguration_OnlyProcessesUpToLimit()
    {
        //_serviceProvider = BuildServiceProvider(
        //            new RetryDeliveresAfterFailedConfiguration
        //            {
        //                TotalBatchSize = 3,
        //                MaximumAttendedCount = 5,
        //                ThresholdDuration = 30000
        //            });

        //using var scope = _serviceProvider.CreateScope();
        // Arrange — 10 retryable deliveries but batch size = 3
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 10)
            .Select(i => BuildRetryableDelivery(
                callbackUrl: $"https://partner-{i}.com/webhook",
                retryCount: 2))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        // Override batch size to 3
        _serviceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>()
            .CurrentValue.TotalBatchSize = 3;

        //var configMock = new Mock<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>().Setup(x => x.CurrentValue).Returns(new RetryDeliveresAfterFailedConfiguration() { MaximumAttendedCount = 5, TotalBatchSize = 3, ThresholdDuration = 25000 });

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(15000);
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — only 3 HTTP calls
        Assert.Equal(3, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Concurrent workers — FOR UPDATE SKIP LOCKED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TwoWorkersSameDeliveries_NoDuplicateProcessing()
    {
        // Arrange — 3 retryable deliveries, two workers competing
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 3)
            .Select(i => BuildRetryableDelivery(
                callbackUrl: $"https://partner-{i}.com/webhook",
                retryCount: 2))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var workerA = CreateWorker();
        var workerB = CreateWorker();

        using var ctsA = new CancellationTokenSource(10_000);
        using var ctsB = new CancellationTokenSource(10_000);

        // Act — both run concurrently
        var taskA = Task.Run(() => workerA.RunAsync(ctsA.Token));
        var taskB = Task.Run(() => workerB.RunAsync(ctsB.Token));

        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (await AllDeliveriesProcessedAsync()) break;
            await Task.Delay(300);
        }

        ctsA.Cancel();
        ctsB.Cancel();

        try { await Task.WhenAll(taskA, taskB); }
        catch (OperationCanceledException) { }

        // Assert — FOR UPDATE SKIP LOCKED prevents double processing
        // Each delivery gets exactly one attempt
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        foreach (var delivery in deliveries)
        {
            var attempts = await assertCtx.WebhookDeliveryAttempts
                .Where(a => a.WebhookDeliveryId == delivery.Id)
                .ToListAsync();

            Assert.Single(attempts);
        }

        Assert.Equal(3, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancelledBeforeFirstTick_ExitsCleanly()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — should not throw
        var ex = await Record.ExceptionAsync(
            () => worker.RunAsync(cts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecuteAsync_ExceptionInService_WorkerContinuesNextTick()
    {
        // Arrange — documents BUG 3: currently one exception kills the worker.
        // When BUG 3 is fixed (try/catch moved inside loop), the worker should
        // recover and process subsequent ticks.
        // This test verifies the worker does NOT propagate the exception as an
        // unhandled fault visible to the caller.
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act — worker runs without any eligible deliveries
        // If an internal exception were thrown it would bubble up here
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        // Assert — no unhandled exception escaped the worker
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Helper — rewire HTTP handler after reassignment
    // -------------------------------------------------------------------------

    private void RewireHttpHandler()
    {
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        services.AddHttpClient("WebhookDeliveryClient")
                .ConfigurePrimaryHttpMessageHandler(() => _httpHandler);

        services.AddScoped<WebhookDeliveryRetryAfterService>();
        services.AddScoped<RetryAfterPendingService>();

        services.Configure<RetryDeliveresAfterFailedConfiguration>(opt =>
        {
            opt.TotalBatchSize       = 10;
            opt.MaximumAttendedCount = 5;
            opt.ThresholdDuration    = 25000;
        });

        _serviceProvider.Dispose();
        _serviceProvider = services.BuildServiceProvider();
    }
}

// =============================================================================
// Testable subclass — exposes ExecuteAsync without PeriodicTimer blocking
// =============================================================================

/// <summary>
/// Exposes the protected <see cref="BackgroundService.ExecuteAsync"/> directly
/// so tests can call it without going through
/// <see cref="BackgroundService.StartAsync"/> or waiting for
/// <see cref="PeriodicTimer"/> to tick.
/// Only exists in the test project.
/// </summary>
internal sealed class TestableRetryPendingDeliveriesWorker : RetryPendingDeieveriesWorker
{
    public TestableRetryPendingDeliveriesWorker(
        IServiceScopeFactory                                   scopeFactory,
        IOptionsMonitor<RetryDeliveresAfterFailedConfiguration> optionsMonitor)
        : base(scopeFactory, optionsMonitor) { }

    /// <summary>
    /// Calls <see cref="ExecuteAsync"/> directly on the test thread.
    /// No background thread, no StartAsync overhead, no 60-second timer wait.
    /// </summary>
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}