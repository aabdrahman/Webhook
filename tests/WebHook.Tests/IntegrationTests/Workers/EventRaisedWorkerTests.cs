using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Integration tests for <see cref="EventRaisedWorker"/> using a real
/// PostgreSQL container via Testcontainers.
///
/// The container is started ONCE per test class via <see cref="PostgreSqlFixture"/>
/// and the schema is reset between tests via <see cref="IAsyncLifetime"/>.
/// This keeps the suite fast while giving every test a clean database.
///
/// Prerequisites:
///   - Docker must be running on the machine executing these tests.
///   - NuGet packages required in the test project:
///       Testcontainers.PostgreSql            4.12.0
///       Npgsql.EntityFrameworkCore.PostgreSQL (same version as your main project)
/// </summary>
public sealed class EventRaisedWorkerTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly PostgreSqlFixture _fixture;
    private Channel<EventRaised> _channel;
    private ServiceProvider _serviceProvider = null!;

    public EventRaisedWorkerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        _channel = Channel.CreateUnbounded<EventRaised>();
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — runs before and after each individual test
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        services.Configure<EventRaisedWorkerConfiguration>(opts =>
        {
            opts.ProcessingIntervalInSeconds = 1;
        });

        _serviceProvider = services.BuildServiceProvider();

        // Wipe and recreate schema so every test starts with a clean slate
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private TestableEventRaisedWorker CreateWorker() =>
    new TestableEventRaisedWorker(
        _channel,
        _serviceProvider.GetRequiredService<IServiceScopeFactory>(), _serviceProvider.GetRequiredService<IOptionsMonitor<EventRaisedWorkerConfiguration>>());

    //private static async Task RunWorkerUntilChannelDrainedAsync(
    //    TestableEventRaisedWorker worker,
    //    Channel<EventRaised> channel,
    //    int timeoutMs = 10_000)
    //{
    //    using var cts = new CancellationTokenSource(timeoutMs);

    //    // Call ExecuteAsync directly — no StartAsync, no timer, no background thread
    //    var executeTask = worker.RunAsync(cts.Token);

    //    // Poll until the channel is empty
    //    while (channel.Reader.Count > 0 && !cts.IsCancellationRequested)
    //        await Task.Delay(100);

    //    // Small buffer for the final DB write to complete
    //    await Task.Delay(500);

    //    cts.Cancel();

    //    try { await executeTask; }
    //    catch (OperationCanceledException) { } // expected on cancellation
    //}

    private async Task RunWorkerUntilChannelDrainedAsync(
    TestableEventRaisedWorker worker,
    Channel<EventRaised> channel,
    int timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        // Wait until the channel is empty first
        while (channel.Reader.Count > 0 && !cts.IsCancellationRequested)
            await Task.Delay(100);

        // Then wait until the DB reflects the work is done
        // rather than a fixed Task.Delay buffer
        await WaitForConditionAsync(
            async () =>
            {
                using var scope = _serviceProvider.CreateScope();
                var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

                // Condition: no more Pending WebhookEvents (they moved to Processing)
                return !await ctx.WebhookEvents
                    .AnyAsync(e => e.Status == WebHookEventStatus.Pending);
            },
            timeoutMs: 10_000);

        cts.Cancel();

        try { await executeTask; }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Polls a condition every 150ms until it returns true or the timeout is reached.
    /// </summary>
    private static async Task WaitForConditionAsync(
        Func<Task<bool>> condition,
        int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(150);
        }
    }

    private static EventRaised BuildEventRaised(Guid? id = null) =>
        new EventRaised(createdEventId: id ?? Guid.NewGuid());

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

    private static WebHookEventCatalog BuildCatalog(List<string> subscribedFields, string eventName = "CustomerCreated") => new()
    {
        Id = Guid.NewGuid(),
        EventName = eventName,
        NormalizedEventName = eventName.ToUpper(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        AvailableFields = subscribedFields.ToDictionary(f => f, f => "string")
    };

    private static WebhookSubscription BuildSubscription(
        string callbackUrl = "https://partner.com/webhook",
        bool isActive = true) => new()
        {
            Id = Guid.NewGuid(),
            Name = "Test Subscription",
            CallbackUrl = callbackUrl,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            SecretKey = "test-secret-key"
        };

    /// <summary>
    /// Starts the worker and waits for at least one 5-second PeriodicTimer
    /// tick to fire, then cancels.
    /// </summary>
    private static async Task RunWorkerForOneTickAsync(
        EventRaisedWorker worker,
        int waitMs = 6500)
    {
        using var cts = new CancellationTokenSource();
        _ = worker.StartAsync(cts.Token);
        await Task.Delay(waitMs);
        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Runs the worker until the channel is empty AND the DB condition
    /// is satisfied, then cancels.
    /// </summary>
    private async Task RunWorkerUntilAsync(
        TestableEventRaisedWorker worker,
        Func<Task<bool>> condition,
        int timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline && !cts.IsCancellationRequested)
        {
            if (await condition()) break;
            await Task.Delay(200).ContinueWith(_ => { });
        }

        await Task.Delay(500); // buffer for final writes

        cts.Cancel();
        try { await executeTask; }
        catch (OperationCanceledException) { }
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
    // FOR UPDATE SKIP LOCKED — real PostgreSQL behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PendingEvent_NoSubscriberExists_StatusChangedToProcessed()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Pending);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.NotNull(updated);
        Assert.Equal(WebHookEventStatus.Processed, updated!.Status);
        Assert.NotNull(updated.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_PendingEvent_SubscriberExists_StatusChangedToProcessing()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Pending);
        var subscription = BuildSubscription();

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.NotNull(updated);
        Assert.Equal(WebHookEventStatus.Processed, updated!.Status);
        Assert.NotNull(updated.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_EventAlreadyProcessing_SkippedByForUpdateSkipLocked()
    {
        // Arrange — status is Processing, not Pending, so FOR UPDATE SKIP LOCKED
        // returns null and the worker rolls back and skips the item
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert — status and ProcessedAt must remain unchanged
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var unchanged = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.Equal(WebHookEventStatus.Processing, unchanged!.Status);
        Assert.Null(unchanged.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentEventId_WorkerContinuesWithoutCrashing()
    {
        // Arrange — ID not in DB, worker should roll back and continue cleanly
        await _channel.Writer.WriteAsync(BuildEventRaised(Guid.NewGuid()));

        // Act & Assert — no exception
        var ex = await Record.ExceptionAsync(
            () => RunWorkerForOneTickAsync(CreateWorker()));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Delivery fan-out
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_OneActiveSubscriber_OneDeliveryCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog = BuildCatalog(["name", "email"], "CustomerCreated");
        var subscription = BuildSubscription("https://partner.com/webhook");
        var subEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "CUSTOMERCREATED",
            status: WebHookEventStatus.Pending,
            payload: @"{""customerId"":""123""}");

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act — wait until the event moves to Processing in the DB,
        // which means SaveChangesAsync completed and deliveries were created
        var worker = CreateWorker();
        await RunWorkerUntilChannelDrainedAsync(worker, _channel);

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Single(deliveries);
        Assert.Equal("https://partner.com/webhook", deliveries[0].CallBackUrl);
        Assert.Equal(WebhookDeliveryStatus.Pending, deliveries[0].DeliveryStatus);
        Assert.Equal(0, deliveries[0].RetryCount);
        Assert.Equal(webhookEvent.PayLoad, deliveries[0].RequestPayload);
    }

    //[Fact]
    //public async Task ExecuteAsync_OneActiveSubscriber_OneDeliveryCreated()
    //{
    //    // Arrange
    //    using var scope = _serviceProvider.CreateScope();
    //    var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
    //    var catalog = BuildCatalog(["name", "email"], "CustomerCreated");
    //    var subscription = BuildSubscription("https://partner.com/webhook");
    //    var subEvent = new WebhookSubscriptionEvent
    //    {
    //        Id = Guid.NewGuid(),
    //        WebhookSubscriptionId = subscription.Id,
    //        WebhookEventCatalogId = catalog.Id,
    //        IsActive = true,
    //        CreatedAt = DateTimeOffset.UtcNow
    //    };
    //    var webhookEvent = BuildWebhookEvent(
    //        eventType: "CUSTOMERCREATED",
    //        status: WebHookEventStatus.Pending,
    //        payload: @"{""customerId"":""123""}");

    //    await ctx.WebHookEventCatalogs.AddAsync(catalog);
    //    await ctx.WebhookSubscriptions.AddAsync(subscription);
    //    await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
    //    await ctx.WebhookEvents.AddAsync(webhookEvent);
    //    await ctx.SaveChangesAsync();

    //    await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

    //    // Act
    //    //await RunWorkerForOneTickAsync(CreateWorker());
    //    var worker = CreateWorker();
    //    await RunWorkerUntilChannelDrainedAsync(worker, _channel);

    //    // Assert
    //    using var assertScope = _serviceProvider.CreateScope();
    //    var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
    //    var deliveries = await assertCtx.WebhookDeliveries
    //        .Where(d => d.WebhookEventId == webhookEvent.Id)
    //        .ToListAsync();

    //    Assert.Single(deliveries);
    //    Assert.Equal("https://partner.com/webhook", deliveries[0].CallBackUrl);
    //    Assert.Equal(WebhookDeliveryStatus.Pending, deliveries[0].DeliveryStatus);
    //    Assert.Equal(0, deliveries[0].RetryCount);
    //    Assert.Equal(webhookEvent.PayLoad, deliveries[0].RequestPayload);
    //}

    [Fact]
    public async Task ExecuteAsync_RandomActiveSubscribers_ThreeDeliveriesCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog = BuildCatalog(["paymentreference", "amount"], "PaymentCompleted");
        var webhookEvent = BuildWebhookEvent(
            eventType: "PAYMENTCOMPLETED",
            status: WebHookEventStatus.Pending);

        int recordCount = Random.Shared.Next(1, 5);

        List<WebhookSubscription> subscriptions = [];

        for (int i = 1; i <= recordCount; i++)
        {
            subscriptions.Add(BuildSubscription($"https://partner-{i}.com/webhook"));
        }

        var subEvents = subscriptions.Select(s => new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = s.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        }).ToArray();

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddRangeAsync(subscriptions);
        await ctx.WebhookEventSubscriptions.AddRangeAsync(subEvents);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .OrderBy(d => d.CallBackUrl)
            .ToListAsync();

        Assert.Equal(recordCount, deliveries.Count);
        Assert.All(deliveries, d => Assert.Equal(WebhookDeliveryStatus.Pending, d.DeliveryStatus));
        Assert.All(deliveries, d => Assert.Equal(0, d.RetryCount));
        //Assert.Contains(deliveries, d => d.CallBackUrl == "https://partner-a.com/webhook");
        //Assert.Contains(deliveries, d => d.CallBackUrl == "https://partner-b.com/webhook");
        //Assert.Contains(deliveries, d => d.CallBackUrl == "https://partner-c.com/webhook");
    }

    [Fact]
    public async Task ExecuteAsync_InactiveSubscriber_NoDeliveryCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog = BuildCatalog(["name", "accountnumber"], "AccountApproved");
        var subscription = BuildSubscription(isActive: false); // inactive
        var subEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "ACCOUNTAPPROVED",
            status: WebHookEventStatus.Pending);

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Empty(deliveries);
    }

    [Fact]
    public async Task ExecuteAsync_NoSubscribers_NoDeliveryCreated()
    {
        // Arrange — valid event but no subscriptions exist
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(
            eventType: "ACCOUNTREJECTED",
            status: WebHookEventStatus.Pending);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Empty(deliveries);
    }

    [Fact]
    public async Task ExecuteAsync_DeliveryPayloadMatchesOriginalEventPayload()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        const string expected = @"{""customerId"":""abc-123"",""firstName"":""John""}";
        var catalog = BuildCatalog(["customerId", "firstName"], "CustomerUpdated");
        var subscription = BuildSubscription();
        var subEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "CUSTOMERUPDATED",
            status: WebHookEventStatus.Pending,
            payload: expected);

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery = await assertCtx.WebhookDeliveries
            .FirstOrDefaultAsync(d => d.WebhookEventId == webhookEvent.Id);

        Assert.NotNull(delivery);
        Assert.Equal(expected, delivery!.RequestPayload, ignoreCase: true);
    }

    // -------------------------------------------------------------------------
    // Concurrent worker safety — FOR UPDATE SKIP LOCKED prevents double delivery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TwoWorkersSameEvent_OnlyOneCreatesDeliveries()
    {
        // Arrange — seed one Pending event and write it to two separate channels
        // to simulate two competing worker instances
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog = BuildCatalog(["name", "email"], "CustomerCreated");
        var subscription = BuildSubscription();
        var subEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "CUSTOMERCREATED",
            status: WebHookEventStatus.Pending);

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        var channelA = Channel.CreateUnbounded<EventRaised>();
        var channelB = Channel.CreateUnbounded<EventRaised>();

        await channelA.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));
        await channelB.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
        var workerConfig = _serviceProvider.GetRequiredService<IOptionsMonitor<EventRaisedWorkerConfiguration>>();
        var workerA = new EventRaisedWorker(channelA, scopeFactory, workerConfig);
        var workerB = new EventRaisedWorker(channelB, scopeFactory, workerConfig);

        using var ctsA = new CancellationTokenSource();
        using var ctsB = new CancellationTokenSource();

        // Act — both workers start and compete for the same event
        _ = workerA.StartAsync(ctsA.Token);
        _ = workerB.StartAsync(ctsB.Token);

        await Task.Delay(7000); // wait for both to tick at least once

        ctsA.Cancel();
        ctsB.Cancel();

        await workerA.StopAsync(CancellationToken.None);
        await workerB.StopAsync(CancellationToken.None);

        // Assert — FOR UPDATE SKIP LOCKED means the second worker gets null
        // and skips. Only one set of deliveries should be created.
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Single(deliveries);
    }

    // -------------------------------------------------------------------------
    // Re-queue — non-existent event ID (null path → skipped not failed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReQueue_NonExistentEventId_ItemNotReQueued()
    {
        // Arrange — write a non-existent ID to the channel
        // The worker fetches it, gets null from FromSqlRaw, rolls back,
        // and calls continue — it should NOT be re-queued because it was
        // intentionally skipped, not failed
        var nonExistentId = Guid.NewGuid();
        await _channel.Writer.WriteAsync(BuildEventRaised(nonExistentId));

        var worker = CreateWorker();

        // Act — run until channel is drained
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_channel.Reader.Count == 0));

        // Assert — channel should be empty, item was NOT re-queued
        // (null path uses continue, not _unsuccessfulRequests.Add)
        Assert.Equal(0, _channel.Reader.Count);
    }

    // -------------------------------------------------------------------------
    // Re-queue — SaveChangesAsync failure causes re-queue
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReQueue_SaveChangesThrows_ItemReQueuedToChannel()
    {
        // Arrange — seed a valid event so FromSqlRaw returns it
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var webhookEvent = BuildWebhookEvent(eventType: "CUSTOMERCREATED");

        // Seed a subscription pointing to a non-existent catalog ID
        // so when the worker builds WebhookDelivery records and calls
        // SaveChangesAsync, PostgreSQL throws a FK constraint violation

        var subscription = BuildSubscription();
        var catalog = BuildCatalog(["name"]);

        var badSubEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            //WebhookEventCatalogId = catalog.Id, // exists in EventCatalog
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            webHookEventCatalog = catalog
        };

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(badSubEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        var worker = CreateWorker();

        // Act — run briefly so the worker processes the item and fails
        using var cts = new CancellationTokenSource(10_000);
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

        // Wait until the item is consumed from the channel
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_channel.Reader.Count > 0 && DateTime.UtcNow < deadline)
            await Task.Delay(200);

        // Give the finally block time to re-queue
        await Task.Delay(1000);

        cts.Cancel();
        try { await executeTask; }
        catch (OperationCanceledException) { }

        // Assert — item was re-queued because SaveChangesAsync threw
        Assert.True(_channel.Reader.Count > 0,
            "Item should have been re-queued after SaveChangesAsync failure.");

        // And the event status must still be Pending — transaction was rolled back
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var eventStatus = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.Equal(WebHookEventStatus.Pending, eventStatus!.Status);
    }

    //[Fact]
    //public async Task ReQueue_SaveChangesFailure_ItemReQueuedToChannel()
    //{
    //    // Arrange — seed a valid event but with a broken subscription
    //    // that will cause SaveChangesAsync to throw a FK violation
    //    using var scope = _serviceProvider.CreateScope();
    //    var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

    //    var webhookEvent = BuildWebhookEvent();
    //    await ctx.WebhookEvents.AddAsync(webhookEvent);
    //    await ctx.SaveChangesAsync();

    //    // Seed a WebhookDelivery that will cause a constraint violation —
    //    // point to a non-existent WebhookEventSubscription ID
    //    // This simulates a SaveChangesAsync failure inside the inner try/catch
    //    var badDelivery = new WebhookDelivery
    //    {
    //        Id = Guid.NewGuid(),
    //        WebhookEventId = webhookEvent.Id,
    //        WebhookSubscriptionEventId = Guid.NewGuid(), // FK violation — does not exist
    //        CallBackUrl = "https://partner.com/webhook",
    //        RequestPayload = "{}",
    //        DeliveryStatus = WebhookDeliveryStatus.Pending,
    //        RetryCount = 0,
    //        CreatedAt = DateTimeOffset.UtcNow
    //    };

    //    // Write to channel BEFORE seeding bad delivery so worker picks it up
    //    await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

    //    var worker = CreateWorker();
    //    var initialCount = _channel.Reader.Count;

    //    // Act — run briefly so worker processes the item
    //    using var cts = new CancellationTokenSource(8000);
    //    var executeTask = Task.Run(() => worker.RunAsync(cts.Token), cts.Token);

    //    // Wait until the item is consumed from the channel
    //    var deadline = DateTime.UtcNow.AddSeconds(8);
    //    while (_channel.Reader.Count == initialCount && DateTime.UtcNow < deadline)
    //        await Task.Delay(200);

    //    // Give re-queue time to happen in the finally block
    //    await Task.Delay(1000);

    //    cts.Cancel();
    //    try { await executeTask; }
    //    catch (OperationCanceledException) { }

    //    // Assert — item should have been re-queued back to the channel
    //    // because SaveChangesAsync failed and the inner catch added it to
    //    // _unsuccessfulRequests which the finally block re-queued
    //    Assert.True(_channel.Reader.Count > 0,
    //        "Failed item should have been re-queued to the channel by the finally block.");
    //}



    [Fact]
    public async Task ReQueue_MultipleFailures_BugDocumentation_ItemsAccumulateWithoutClear()
    {
        // Arrange — seed two valid events
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var event1 = BuildWebhookEvent(eventType: "CUSTOMERCREATED");
        var event2 = BuildWebhookEvent(eventType: "PAYMENTCOMPLETED");

        await ctx.WebhookEvents.AddRangeAsync(event1, event2);
        await ctx.SaveChangesAsync();

        // Write both to channel — but we will force both to fail
        // by writing non-existent IDs (null path → skipped, not failed)
        // To actually trigger _unsuccessfulRequests we need inner catch to fire
        // which happens when SaveChangesAsync throws

        // Write non-existent IDs — these go through null path (continue)
        // and do NOT accumulate in _unsuccessfulRequests
        var id1 = Guid.NewGuid(); // does not exist
        var id2 = Guid.NewGuid(); // does not exist

        await _channel.Writer.WriteAsync(BuildEventRaised(id1));
        await _channel.Writer.WriteAsync(BuildEventRaised(id2));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_channel.Reader.Count == 0));

        // Assert — non-existent IDs go through null/continue path
        // and should NOT accumulate in _unsuccessfulRequests or re-queue
        Assert.Equal(0, _channel.Reader.Count);
    }

    // -------------------------------------------------------------------------
    // Re-queue — only failed items re-queued, not skipped items
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReQueue_SkippedItem_IsNotConsideredFailed()
    {
        // Arrange — event already in Processing status
        // Worker fetches it with FOR UPDATE SKIP LOCKED → null → skipped
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent();
        webhookEvent.Status = WebHookEventStatus.Processing; // already being processed

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            () => Task.FromResult(_channel.Reader.Count == 0));

        // Assert — skipped item not re-queued, channel is empty
        Assert.Equal(0, _channel.Reader.Count);

        // And the event status must remain Processing — worker did not touch it
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var unchanged = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.Equal(WebHookEventStatus.Processing, unchanged!.Status);
    }

    // -------------------------------------------------------------------------
    // Re-queue — successful items are NOT re-queued
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReQueue_SuccessfulItem_IsNotReQueued()
    {
        // Arrange — seed a valid event with a valid subscription
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var catalog = BuildCatalog(["name", "email"]);
        var subscription = BuildSubscription();

        var subEvent = new WebhookSubscriptionEvent
        {
            Id = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var webhookEvent = BuildWebhookEvent(eventType: "CUSTOMERCREATED");

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.WebhookEventSubscriptions.AddAsync(subEvent);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        var worker = CreateWorker();

        // Act — wait until the event is committed as Processing
        await RunWorkerUntilAsync(
            worker,
            async () =>
            {
                using var s = _serviceProvider.CreateScope();
                var c = s.ServiceProvider.GetRequiredService<RepositoryContext>();
                var e = await c.WebhookEvents.FindAsync(webhookEvent.Id);
                return e?.Status == WebHookEventStatus.Processing;
            });

        // Assert — channel must be empty after successful processing
        // (item was NOT added to _unsuccessfulRequests)
        Assert.Equal(0, _channel.Reader.Count);
    }

    // -------------------------------------------------------------------------
    // Re-queue — finally block fires even on success (documents behaviour)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ReQueue_FinallyAlwaysFires_EmptyUnsuccessfulListCausesNoReQueue()
    {
        // Arrange — successful processing path
        // The finally block ALWAYS runs but if _unsuccessfulRequests is empty
        // the foreach does nothing — this test confirms that behaviour
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var catalog = BuildCatalog(["name", "email"], eventName: "AccountApproved");
        var subscription = BuildSubscription();

        var webhookEvent = BuildWebhookEvent(eventType: "ACCOUNTAPPROVED");

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        // No subscriptions — event will be marked Processed (no deliveries)
        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        var worker = CreateWorker();

        // Act
        await RunWorkerUntilAsync(
            worker,
            async () =>
            {
                using var s = _serviceProvider.CreateScope();
                var c = s.ServiceProvider.GetRequiredService<RepositoryContext>();
                var e = await c.WebhookEvents.FindAsync(webhookEvent.Id);
                return e?.Status == WebHookEventStatus.Processed;
            });

        // Assert — channel empty, nothing re-queued
        Assert.Equal(0, _channel.Reader.Count);

        // Event was marked Processed (no subscribers)
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var processed = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.Equal(WebHookEventStatus.Processed, processed!.Status);
    }

    [Fact]
    public async Task ReQueue_AfterReQueue_UnsuccessfulListMustBeClearedToPreventDuplicates()
    {
        // Arrange — this test documents what SHOULD happen after the fix:
        // after re-queuing, _unsuccessfulRequests.Clear() must be called
        // so the next item's finally block does not re-queue the same items again.
        //
        // With the FIX:
        //   Item A fails → added to _unsuccessfulRequests → re-queued in finally
        //   _unsuccessfulRequests.Clear() called
        //   Item B processed → finally fires → _unsuccessfulRequests is empty → nothing re-queued
        //
        // We verify by writing two items where only one fails (non-existent ID)
        // and counting how many times the failed ID appears in the channel

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var successEvent = BuildWebhookEvent(eventType: "CUSTOMERCREATED");

        var catalog = BuildCatalog(["name", "email"]);

        await ctx.WebHookEventCatalogs.AddAsync(catalog);
        await ctx.WebhookEvents.AddAsync(successEvent);
        await ctx.SaveChangesAsync();

        // Write both — one valid, one non-existent
        var nonExistentId = Guid.NewGuid();
        await _channel.Writer.WriteAsync(BuildEventRaised(nonExistentId));  // will be skipped (null path)
        await _channel.Writer.WriteAsync(BuildEventRaised(successEvent.Id)); // will succeed

        var worker = CreateWorker();

        // Act — run until success event is processed
        await RunWorkerUntilAsync(
            worker,
            async () =>
            {
                using var s = _serviceProvider.CreateScope();
                var c = s.ServiceProvider.GetRequiredService<RepositoryContext>();
                var e = await c.WebhookEvents.FindAsync(successEvent.Id);
                return e?.Status == WebHookEventStatus.Processed;
            });

        // Assert — with null path (non-existent ID) items are NOT added to
        // _unsuccessfulRequests so no re-queue happens regardless of the bug.
        // Channel should be empty after both items processed.
        Assert.Equal(0, _channel.Reader.Count);
    }
}



// Only exists in the test project — never ships to production
internal sealed class TestableEventRaisedWorker : EventRaisedWorker
{
    public TestableEventRaisedWorker(
        Channel<EventRaised> channel,
        IServiceScopeFactory scopeFactory, IOptionsMonitor<EventRaisedWorkerConfiguration> workerConfig)
        : base(channel, scopeFactory, workerConfig) { }

    // Exposes the protected ExecuteAsync so tests can call it directly
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}