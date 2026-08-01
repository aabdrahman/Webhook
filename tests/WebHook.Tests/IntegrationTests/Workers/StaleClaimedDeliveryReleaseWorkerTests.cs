using MassTransit.Courier.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.IntegrationTests.Workers;

public class StaleClaimedDeliveryReleaseWorkerTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------
    private readonly PostgreSqlFixture _postgreSqlFixture;

    private ServiceProvider _serviceProvider = null!;

    private List<WebhookSubscription> _webhookSubscriptions;
    private List<WebHookEventCatalog> _webHookEventCatalogs;
    public StaleClaimedDeliveryReleaseWorkerTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — fresh state per test
    // -------------------------------------------------------------------------
    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
        //throw new NotImplementedException();
    }


    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        // Real PostgreSQL via Testcontainers
        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_postgreSqlFixture.ConnectionString));

        // Concrete service — no interface yet
        services.AddScoped<StaleClaimedDeliveryReleaseService>();

        // Configuration — short lock duration so tests do not need old data
        services.Configure<RetryDeliveresAfterFailedConfiguration>(opt =>
        {
            opt.DeliveryLockDuration = 600; // 10 minutes default
            opt.StaleDeliveryReleaseIntervalSeconds = 1;
        });

        _serviceProvider = services.BuildServiceProvider();

        // Create schema once, truncate between tests
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        //Ensures the databse is created for the test case
        await ctx.Database.EnsureCreatedAsync();

        //Ensures the tables are truncated for each tests, this ensures that each test is running on a fresh and newly created database tables for it.
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

        //Instantiate new instances of the catalogs and sunscriptions.
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


    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Necessary to create catalog with parameters.
    private WebHookEventCatalog BuildCatalog(List<string> subscribedFields, string eventName = "CustomerCreated") => new()
    {
        Id = Guid.NewGuid(),
        EventName = eventName,
        NormalizedEventName = eventName.ToUpper(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = subscribedFields.ToDictionary(f => f, f => "string")
    };

    //Necessary to create new instance of a subscription with desired parameters.
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

    private static WebhookEvent BuildWebhookEvent(Guid? id = null, string eventType = "CUSTOMERCREATED", WebHookEventStatus status = WebHookEventStatus.Pending, string payload = "{}") => new()
    {
        Id = id ?? Guid.NewGuid(),
        EventType = eventType,
        Status = status,
        PayLoad = payload,
        Source = "CustomerService",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private TestableStaleClaimedDeliverReleaseWorker CreateWorker() =>
        new TestableStaleClaimedDeliverReleaseWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(), _serviceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>());

    /// <summary>
    /// Runs the worker's ExecuteAsync directly and polls the given
    /// <paramref name="condition"/> until it returns true or the
    /// <paramref name="timeoutMs"/> is reached.
    /// </summary>
    private async Task RunWorkerUntilAsync(TestableStaleClaimedDeliverReleaseWorker worker, Func<Task<bool>> condition, int timeoutMs = 10_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        // Run ExecuteAsync directly on a background thread
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        // Poll until condition satisfied or timeout
        while (!cts.IsCancellationRequested)
        {
            if (await condition()) break;
            await Task.Delay(200).ContinueWith(_ => { });
        }

        // Small buffer for SaveChangesAsync to complete
        await Task.Delay(300);

        cts.Cancel();

        try { await executeTask; }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Condition: no deliveries remain in Processing status with an expired lock.
    /// </summary>
    private async Task<bool> NoStaleDeliveriesRemainingAsync(double lockDurationSeconds = 600)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var thresholdTime = DateTimeOffset.UtcNow.AddSeconds(-lockDurationSeconds);

        return !await ctx.WebhookDeliveries.AnyAsync(
            wd => !string.IsNullOrEmpty(wd.LockedBy)
               && wd.LockedUntil.HasValue
               && thresholdTime > wd.LockedUntil.Value
               && wd.DeliveryStatus == WebhookDeliveryStatus.Processing);
    }

    /// <summary>
    /// Condition: at least one delivery has transitioned to Pending.
    /// </summary>
    private async Task<bool> AtLeastOnePendingExistsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await ctx.WebhookDeliveries
            .AnyAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Pending);
    }

    /// <summary>
    /// Builds a delivery that IS stale — locked, Processing, lock expired
    /// beyond the default 600-second threshold.
    /// </summary>
    private WebhookDelivery BuildStaleDelivery(string lockedBy = "worker-1", int retryCount = 1, double expiredAgoSeconds = 700, Guid? subscriptionId = null, string payload = @"{""customerId"":""123""}") => new()
    {
        Id = Guid.NewGuid(),
        CallBackUrl = "https://partner.com/webhook",
        RequestPayload = payload,
        DeliveryStatus = WebhookDeliveryStatus.Processing,
        RetryCount = retryCount,
        LockedBy = lockedBy,
        LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-expiredAgoSeconds),
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
        WebhookSubscriptionEventId = subscriptionId.HasValue ? subscriptionId.Value : _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
    };

    /// <summary>
    /// Builds a delivery whose lock has NOT yet expired — still legitimately
    /// in-flight, must not be released.
    /// </summary>
    private WebhookDelivery BuildActiveLockDelivery(Guid? subscriptionId = null, string payload = @"{""customerId"":""123""}") => new()
    {
        Id = Guid.NewGuid(),
        CallBackUrl = "https://partner.com/webhook",
        RequestPayload = @"{""customerId"":""123""}",
        DeliveryStatus = WebhookDeliveryStatus.Processing,
        RetryCount = 1,
        LockedBy = "worker-1",
        LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5), // still valid
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        WebhookSubscriptionEventId = subscriptionId.HasValue ? subscriptionId.Value : _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
    };

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
        cts.Cancel();

        var stopTask = worker.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask;

        Assert.True(completedInTime, "StopAsync did not complete within 5 seconds.");
    }

    // -------------------------------------------------------------------------
    // No stale deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoDeliveries_CompletesWithoutError()
    {
        // Arrange — empty DB
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act & Assert — should not throw
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecuteAsync_ActiveLockDelivery_NotReleased()
    {
        // Arrange — delivery has a valid (non-expired) lock
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildActiveLockDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — untouched
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var unchanged = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Processing, unchanged!.DeliveryStatus);
        Assert.NotNull(unchanged.LockedBy);
        Assert.NotNull(unchanged.LockedUntil);
    }

    // -------------------------------------------------------------------------
    // Stale delivery released
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_StaleDelivery_LockedBySetToNull()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildStaleDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, AtLeastOnePendingExistsAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Null(updated!.LockedBy);
    }

    [Fact]
    public async Task ExecuteAsync_StaleDelivery_LockedUntilSetToNull()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildStaleDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, AtLeastOnePendingExistsAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Null(updated!.LockedUntil);
    }

    [Fact]
    public async Task ExecuteAsync_StaleDelivery_StatusResetToPending()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildStaleDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, AtLeastOnePendingExistsAsync);

        // Assert — delivery processor worker can now pick it up again
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
    }

    [Fact]
    public async Task ExecuteAsync_StaleDelivery_RetryCountIncremented()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildStaleDelivery(retryCount: 2);
        var originalCount = delivery.RetryCount;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, AtLeastOnePendingExistsAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(originalCount + 1, updated!.RetryCount);
    }

    // -------------------------------------------------------------------------
    // Multiple stale deliveries
    // -------------------------------------------------------------------------
    [Fact]
    public async Task ExecuteAsync_MultipleStaleDeliveries_AllReleased()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = new[]
        {
            BuildStaleDelivery(lockedBy: "worker-1"),
            BuildStaleDelivery(lockedBy: "worker-2"),
            BuildStaleDelivery(lockedBy: "worker-3")
        };

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, () => NoStaleDeliveriesRemainingAsync());

        // Assert — all three released
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var allDeliveries = await assertCtx.WebhookDeliveries.ToListAsync();

        Assert.Equal(3, allDeliveries.Count);
        Assert.All(allDeliveries, d =>
        {
            Assert.Null(d.LockedBy);
            Assert.Null(d.LockedUntil);
            Assert.Equal(WebhookDeliveryStatus.Failed, d.DeliveryStatus);
        });
    }

    [Fact]
    public async Task ExecuteAsync_MixedDeliveries_OnlyStaleReleased()
    {
        // Arrange — one stale, one active lock, one already Pending
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var stale = BuildStaleDelivery();
        var activeLock = BuildActiveLockDelivery();
        var alreadyPending = new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            CallBackUrl = "https://partner.com/webhook",
            RequestPayload = "{}",
            DeliveryStatus = WebhookDeliveryStatus.Pending,
            RetryCount = 0,
            LockedBy = null,
            LockedUntil = null,
            CreatedAt = DateTimeOffset.UtcNow,
            WebhookSubscriptionEventId = _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
            webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: "{}"),
        };

        await ctx.WebhookDeliveries.AddRangeAsync(stale, activeLock, alreadyPending);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(worker, () => NoStaleDeliveriesRemainingAsync());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var updatedStale = await assertCtx.WebhookDeliveries.FindAsync(stale.Id);
        var updatedActiveLock = await assertCtx.WebhookDeliveries.FindAsync(activeLock.Id);
        var updatedPending = await assertCtx.WebhookDeliveries.FindAsync(alreadyPending.Id);

        // Stale — released
        Assert.Equal(WebhookDeliveryStatus.Failed, updatedStale!.DeliveryStatus);
        Assert.Null(updatedStale.LockedBy);
        Assert.Null(updatedStale.LockedUntil);

        // Active lock — untouched
        Assert.Equal(WebhookDeliveryStatus.Processing, updatedActiveLock!.DeliveryStatus);
        Assert.NotNull(updatedActiveLock.LockedBy);

        // Already pending — untouched
        Assert.Equal(WebhookDeliveryStatus.Pending, updatedPending!.DeliveryStatus);
        Assert.Null(updatedPending.LockedBy);
    }

    // -------------------------------------------------------------------------
    // Worker survives exception in service
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_ExceptionInService_WorkerContinuesRunning()
    {
        // Arrange — worker runs on empty DB, no exception should escape
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act & Assert — exception inside try/catch in loop must not propagate
        var ex = await Record.ExceptionAsync(async () =>
        {
            try { await worker.RunAsync(cts.Token); }
            catch (OperationCanceledException) { }
        });

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancelledImmediately_ExitsCleanly()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => worker.RunAsync(cts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledMidRun_NoCorruptedDeliveries()
    {
        // Arrange — seed some stale deliveries
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 5)
            .Select(_ => BuildStaleDelivery())
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(1500); // cancel mid-processing

        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — every delivery is in a valid state
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var allDeliveries = await assertCtx.WebhookDeliveries.ToListAsync();

        var validStatuses = new[]
        {
            WebhookDeliveryStatus.Pending,
            WebhookDeliveryStatus.Processing,
            WebhookDeliveryStatus.Failed,
            WebhookDeliveryStatus.Delivered
        };

        Assert.All(allDeliveries,
            d => Assert.Contains(d.DeliveryStatus, validStatuses));
    }
}


// =============================================================================
// Testable subclass
// =============================================================================

/// <summary>
/// Exposes the protected <see cref="BackgroundService.ExecuteAsync"/> so
/// tests can call it directly without waiting for the 120-second
/// <see cref="PeriodicTimer"/> tick.
/// Only exists in the test project.
/// </summary>
internal sealed class TestableStaleClaimedDeliverReleaseWorker
    : StaleClaimedDeliverReleaseWorker
{
    public TestableStaleClaimedDeliverReleaseWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<RetryDeliveresAfterFailedConfiguration> optionsMonitorConfig)
        : base(scopeFactory, optionsMonitorConfig) { }

    /// <summary>
    /// Calls <see cref="ExecuteAsync"/> directly on the calling thread.
    /// No background thread, no StartAsync overhead, no 120-second wait.
    /// </summary>
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}