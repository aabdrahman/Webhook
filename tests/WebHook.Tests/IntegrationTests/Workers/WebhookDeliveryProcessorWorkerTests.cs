using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.Tests.Utilities;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Integration tests for <see cref="WebhookDeliveryProcessorWorker"/>.
///
/// Uses:
///   - Testcontainers PostgreSQL — real DB for FromSqlRaw + FOR UPDATE SKIP LOCKED
///   - TestableWebhookDeliveryProcessorWorker — exposes ExecuteAsync directly
///     so tests do not wait for PeriodicTimer ticks
///   - MockHttpMessageHandler — controls consumer endpoint HTTP responses
///   - WorkerConfiguration.DeliveryProcessorIntervalSeconds = 1 — short tick
///     for tests that do go through StartAsync
///
/// Prerequisites:
///   - Docker running
///   - Testcontainers.PostgreSql 4.12.0
///   - Npgsql.EntityFrameworkCore.PostgreSQL
/// </summary>
public sealed class WebhookDeliveryProcessorWorkerTests
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
    private List<string> _encryptedSecrets = [];

    public WebhookDeliveryProcessorWorkerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — fresh state per test
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        // Fresh handler per test so CallCount is always accurate
        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);

        var services = new ServiceCollection();

        // Real PostgreSQL via Testcontainers
        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        // Named HttpClient backed by the mock handler
        services.AddHttpClient("WebhookDeliveryClient")
                .ConfigurePrimaryHttpMessageHandler(() => _httpHandler);

        // Concrete services
        services.AddScoped<WebhookDeliveryRetryAfterService>();
        services.AddScoped<WebhookDeliveryProcessorService>();

        //Add the signature and encrptor services.
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();

        // Worker configuration — 1 second tick so timer-based tests are fast
        services.Configure<WebhookDeliveryWorkerConfiguration>(opt =>
        {
            opt.DeliveryProcessorIntervalSeconds = 1;
            opt.TotalBatchSize = 10;
            opt.DeliveryLockDuration = 60;
        });

        services.AddSingleton(new WorkerLivenessTracker(TimeSpan.FromSeconds(15)));

        _serviceProvider = services.BuildServiceProvider();

        // Create schema once, truncate between tests
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var encryptor = _serviceProvider.GetRequiredService<IEncryptionService>();
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

        _encryptedSecrets.AddRange
        (
            Enumerable.Range(1, 5).Select(i => encryptor.Encrypt(Random.Shared.GetHexString(32))).ToList()
        );

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

    private TestableWebhookDeliveryProcessorWorker CreateWorker() =>
        new TestableWebhookDeliveryProcessorWorker(
            _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            _serviceProvider.GetRequiredService<IOptionsMonitor<WebhookDeliveryWorkerConfiguration>>(), _serviceProvider.GetRequiredService<WorkerLivenessTracker>());

    private WebhookDelivery BuildPendingDelivery(
        string callbackUrl = "https://partner.com/webhook",
        string payload = @"{""customerId"":""123""}",
        int retryCount = 0, Guid? subscriptionId = null) => new()
        {
            Id = Guid.NewGuid(),
            CallBackUrl = callbackUrl,
            RequestPayload = payload,
            DeliveryStatus = WebhookDeliveryStatus.Pending,
            RetryCount = retryCount,
            CreatedAt = DateTimeOffset.UtcNow,
            WebhookSubscriptionEventId = subscriptionId ?? _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
            webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
            WebhookDeliveryAttempts = new List<WebhookDeliveryAttempt>()
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
            SecretKey = _encryptedSecrets.OrderBy(x => Guid.NewGuid()).First(),
            WebhookEvents = eventIds.Select(x => new WebhookSubscriptionEvent() { WebhookSubscriptionId = entityId, WebhookEventCatalogId = x, CreatedAt = DateTimeOffset.UtcNow, IsActive = true }).ToList()
        };
    }

    /// <summary>
    /// Runs <see cref="ExecuteAsync"/> directly via the testable subclass.
    /// Cancels after <paramref name="timeoutMs"/> or when the DB shows
    /// all pending deliveries have been processed — whichever comes first.
    /// </summary>
    private async Task RunWorkerUntilProcessedAsync(
        TestableWebhookDeliveryProcessorWorker worker,
        Func<Task<bool>> untilCondition,
        int timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        // Run ExecuteAsync on a background thread so we can poll
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        // Poll until the condition is satisfied or we hit the timeout
        while (!cts.IsCancellationRequested)
        {
            if (await untilCondition())
                break;

            await Task.Delay(200, cts.Token).ContinueWith(_ => { }); // swallow cancellation
        }

        // Small buffer for SaveChangesAsync to complete
        await Task.Delay(500);

        cts.Cancel();

        try { await executeTask; }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Condition: all deliveries in the DB have moved away from Pending status.
    /// </summary>
    private async Task<bool> AllDeliveriesProcessedAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return !await ctx.WebhookDeliveries
            .AnyAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Pending);
    }

    /// <summary>
    /// Condition: at least <paramref name="count"/> delivery attempts exist.
    /// </summary>
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
        cts.Cancel();

        var stopTask = worker.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask;

        Assert.True(completedInTime, "StopAsync did not complete within 5 seconds.");
    }

    // -------------------------------------------------------------------------
    // No pending deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_NoPendingDeliveries_NoHttpCallMade()
    {
        // Arrange — empty DB
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource(3000);

        // Act — run for 3 seconds, no items to process
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(0, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Successful delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_200Response_StatusChangedToDelivered()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(worker, AllDeliveriesProcessedAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updated!.DeliveryStatus);
        Assert.NotNull(updated.DeliveredAt);
        Assert.Equal(1, _httpHandler.CallCount);
    }

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_200Response_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: "OK");
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(
            worker,
            () => AtLeastNAttemptsExistAsync(1));

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal(StatusCodes.Status200OK.ToString(), attempts[0].HttpResponseCode);
        Assert.Equal(1, attempts[0].AttemptedCount);
        Assert.True(attempts[0].Duration >= 0);
    }

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_DeliveredAtIsAfterCreatedAt()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(worker, AllDeliveriesProcessedAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.DeliveredAt);
        Assert.True(updated.DeliveredAt >= updated.CreatedAt);
    }

    // -------------------------------------------------------------------------
    // Failed delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_500Response_StatusChangedToFailed()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(worker, AllDeliveriesProcessedAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
        Assert.Null(updated.DeliveredAt);
        Assert.NotNull(updated.NextRetryAt);
    }

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_500Response_NextRetryAtIsInFuture()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery(retryCount: 0);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        RewireHttpHandler();

        var worker = CreateWorker();
        var beforeRun = DateTimeOffset.UtcNow;

        // Act
        await RunWorkerUntilProcessedAsync(worker, AllDeliveriesProcessedAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.NextRetryAt);
        Assert.True(updated.NextRetryAt > beforeRun);
    }

    [Fact]
    public async Task ExecuteAsync_PendingDelivery_500Response_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            responseBody: "Service Unavailable");
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(
            worker,
            () => AtLeastNAttemptsExistAsync(1));

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal(StatusCodes.Status500InternalServerError.ToString(), attempts[0].HttpResponseCode);
    }

    // -------------------------------------------------------------------------
    // Batch processing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_MultipleDeliveries_AllProcessedInOneTick()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 5)
            .Select(i => BuildPendingDelivery($"https://partner-{i}.com/webhook"))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(worker, AllDeliveriesProcessedAsync);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var allDeliveries = await assertCtx.WebhookDeliveries.ToListAsync();

        Assert.Equal(5, _httpHandler.CallCount);
        Assert.All(allDeliveries,
            d => Assert.Equal(WebhookDeliveryStatus.Delivered, d.DeliveryStatus));
    }

    [Fact]
    public async Task ExecuteAsync_BatchSizeLimit_OnlyProcessesUpToLimit()
    {
        // Arrange — 10 deliveries but batch size is 3
        using (var scope = _serviceProvider.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var deliveries = Enumerable.Range(1, 10)
                .Select(i => BuildPendingDelivery($"https://partner-{i}.com/webhook"))
                .ToList();

            await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
            await ctx.SaveChangesAsync();
        }


        // Override batch size to 3
        var currentWorkerConfig = (_serviceProvider.GetRequiredService<IOptionsMonitor<WebhookDeliveryWorkerConfiguration>>()).CurrentValue;
        currentWorkerConfig.TotalBatchSize = 3;
        currentWorkerConfig.DeliveryProcessorIntervalSeconds = 1;

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler(deliveryWorkerConfig: currentWorkerConfig);

        var worker = CreateWorker();

        // Run for just one tick worth of time
        using var cts = new CancellationTokenSource(2000);
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — only 3 HTTP calls, 7 still pending
        Assert.Equal(3, _httpHandler.CallCount);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var delivered = await assertCtx.WebhookDeliveries
            .CountAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Delivered);
        var pending = await assertCtx.WebhookDeliveries
            .CountAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Pending);

        Assert.Equal(3, delivered);
        Assert.Equal(7, pending);
    }

    [Fact]
    public async Task ExecuteAsync_MixedStatusDeliveries_OnlyPendingProcessed()
    {
        // Arrange — one Pending, one already Delivered, one Failed
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var pending = BuildPendingDelivery("https://partner.com/webhook");
        var alreadyDelivered = BuildPendingDelivery("https://other.com/webhook");
        var failed = BuildPendingDelivery("https://another.com/webhook");

        alreadyDelivered.DeliveryStatus = WebhookDeliveryStatus.Delivered;
        alreadyDelivered.DeliveredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        failed.DeliveryStatus = WebhookDeliveryStatus.Failed;

        await ctx.WebhookDeliveries.AddRangeAsync(pending, alreadyDelivered, failed);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilProcessedAsync(
            worker,
            async () =>
            {
                using var s = _serviceProvider.CreateScope();
                var c = s.ServiceProvider.GetRequiredService<RepositoryContext>();
                var p = await c.WebhookDeliveries.FindAsync(pending.Id);
                return p?.DeliveryStatus != WebhookDeliveryStatus.Pending;
            }, timeoutMs: 2500);

        // Assert — only the Pending delivery was processed
        Assert.Equal(1, _httpHandler.CallCount);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var updatedPending = await assertCtx.WebhookDeliveries.FindAsync(pending.Id);
        var updatedDelivered = await assertCtx.WebhookDeliveries.FindAsync(alreadyDelivered.Id);
        var updatedFailed = await assertCtx.WebhookDeliveries.FindAsync(failed.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updatedPending!.DeliveryStatus);
        Assert.Equal(WebhookDeliveryStatus.Delivered, updatedDelivered!.DeliveryStatus); // unchanged
        Assert.Equal(WebhookDeliveryStatus.Failed, updatedFailed!.DeliveryStatus);    // unchanged
    }

    // -------------------------------------------------------------------------
    // Concurrent workers — FOR UPDATE SKIP LOCKED
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TwoWorkersSameDeliveries_NoDuplicateProcessing()
    {
        // Arrange — seed 3 Pending deliveries
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 3)
            .Select(i => BuildPendingDelivery($"https://partner-{i}.com/webhook"))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        // Two workers competing for the same deliveries
        var workerA = CreateWorker();
        var workerB = CreateWorker();

        using var ctsA = new CancellationTokenSource(25000);
        using var ctsB = new CancellationTokenSource(25000);

        // Act — both run concurrently
        var taskA = Task.Run(() => workerA.RunAsync(ctsA.Token));
        var taskB = Task.Run(() => workerB.RunAsync(ctsB.Token));

        // Wait until all deliveries are processed
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await AllDeliveriesProcessedAsync()) break;
            await Task.Delay(500);
        }

        ctsA.Cancel();
        ctsB.Cancel();

        try { await Task.WhenAll(taskA, taskB); }
        catch (OperationCanceledException) { }

        // Assert — FOR UPDATE SKIP LOCKED prevents double processing
        // Each delivery should have exactly ONE attempt record
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        foreach (var delivery in deliveries)
        {
            var attempts = await assertCtx.WebhookDeliveryAttempts
                .Where(a => a.WebhookDeliveryId == delivery.Id)
                .ToListAsync();

            //Assert.Single(attempts, $"The delivery attempt count is: {attempts.Count} for delivery: {delivery.Id}",); // exactly one attempt per delivery — no duplicates
            Assert.Single(attempts);
            Assert.True(attempts.Count == 1, $"Attempt for delivery: {delivery.Id} has total count: {attempts.Count}");

        }

        // Total HTTP calls == total deliveries — no delivery was called twice
        Assert.Equal(3, _httpHandler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ExitsCleanly()
    {
        // Arrange
        var worker = CreateWorker();
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel before starting

        // Act & Assert — should not throw
        var ex = await Record.ExceptionAsync(
            () => worker.RunAsync(cts.Token));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledMidProcessing_DoesNotLeaveDeliveriesCorrupted()
    {
        // Arrange — seed deliveries
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = Enumerable.Range(1, 5)
            .Select(i => BuildPendingDelivery($"https://partner-{i}.com/webhook"))
            .ToList();

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        _httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        RewireHttpHandler();

        var worker = CreateWorker();

        // Act — cancel after 1.5 seconds (mid-processing)
        using var cts = new CancellationTokenSource(2500);
        try { await worker.RunAsync(cts.Token); }
        catch (OperationCanceledException) { }

        // Assert — no delivery should be in an undefined state
        // Each delivery must be either Pending, Delivered, or Failed — never corrupted
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var allDeliveries = await assertCtx.WebhookDeliveries.ToListAsync();

        var validStatuses = new[]
        {
            WebhookDeliveryStatus.Pending,
            WebhookDeliveryStatus.Delivered,
            WebhookDeliveryStatus.Failed
        };

        Assert.All(allDeliveries,
            d => Assert.Contains(d.DeliveryStatus, validStatuses));
    }

    // -------------------------------------------------------------------------
    // Helper — rewire HTTP handler after reassigning _httpHandler
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds the service provider's named HttpClient to use the
    /// current <see cref="_httpHandler"/>. Called after reassigning
    /// <see cref="_httpHandler"/> in tests that need a specific response.
    /// </summary>
    private void RewireHttpHandler(WebhookDeliveryWorkerConfiguration deliveryWorkerConfig = null)
    {
        // Rebuild the service provider with the updated handler
        var services = new ServiceCollection();

        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        services.AddHttpClient("WebhookDeliveryClient")
                .ConfigurePrimaryHttpMessageHandler(() => _httpHandler);

        services.AddScoped<WebhookDeliveryRetryAfterService>();
        services.AddScoped<WebhookDeliveryProcessorService>();

        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();

        if (deliveryWorkerConfig is null)
        {
            services.Configure<WebhookDeliveryWorkerConfiguration>(opt =>
            {
                opt.DeliveryProcessorIntervalSeconds = 1;
                opt.TotalBatchSize = 10;
                opt.DeliveryLockDuration = 60;
            });
        }
        else if (deliveryWorkerConfig is not null)
        {
            services.Configure<WebhookDeliveryWorkerConfiguration>(opt =>
            {
                opt.DeliveryProcessorIntervalSeconds = deliveryWorkerConfig.DeliveryProcessorIntervalSeconds;
                opt.TotalBatchSize = deliveryWorkerConfig.TotalBatchSize;
                opt.DeliveryLockDuration = deliveryWorkerConfig.DeliveryLockDuration;

            });
        }

        services.AddSingleton(new WorkerLivenessTracker(TimeSpan.FromSeconds(15)));

        _serviceProvider.Dispose();
        _serviceProvider = services.BuildServiceProvider();
    }
}

// =============================================================================
// Testable subclass — exposes ExecuteAsync without waiting for PeriodicTimer
// =============================================================================

/// <summary>
/// Exposes the protected <see cref="BackgroundService.ExecuteAsync"/> so
/// tests can call it directly without going through
/// <see cref="BackgroundService.StartAsync"/>.
/// Only exists in the test project — never ships to production.
/// </summary>
internal sealed class TestableWebhookDeliveryProcessorWorker : WebhookDeliveryProcessorWorker
{
    public TestableWebhookDeliveryProcessorWorker(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<WebhookDeliveryWorkerConfiguration> options, WorkerLivenessTracker workerLivenessTracker)
        : base(scopeFactory, options, workerLivenessTracker) { }

    /// <summary>
    /// Calls <see cref="ExecuteAsync"/> directly on the calling thread.
    /// No background thread, no <see cref="BackgroundService.StartAsync"/> overhead.
    /// </summary>
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}
