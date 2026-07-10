using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using Xunit;

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

        _serviceProvider = services.BuildServiceProvider();

        // Wipe and recreate schema so every test starts with a clean slate
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();
    }

    //public async Task InitializeAsync()
    //{
    //    // Fresh channel — no leftover items between tests
    //    _channel = Channel.CreateUnbounded<EventRaised>();

    //    var services = new ServiceCollection();
    //    services.AddDbContext<RepositoryContext>(opt =>
    //        opt.UseNpgsql(_fixture.ConnectionString));

    //    _serviceProvider = services.BuildServiceProvider();

    //    using var scope = _serviceProvider.CreateScope();
    //    var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

    //    await ctx.Database.EnsureCreatedAsync();

    //    // Truncate all tables — faster and more reliable than drop/recreate
    //    await ctx.Database.ExecuteSqlRawAsync(@"
    //        TRUNCATE TABLE
    //            ""WebhookDeliveries"",
    //            ""WebhookEventSubscriptions"",
    //            ""WebhookSubscriptions"",
    //            ""WebhookEvents"",
    //            ""WebHookEventCatalogs""
    //        RESTART IDENTITY CASCADE;
    //    ");
    //}

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private TestableEventRaisedWorker CreateWorker() =>
    new TestableEventRaisedWorker(
        _channel,
        _serviceProvider.GetRequiredService<IServiceScopeFactory>());

    private static async Task RunWorkerUntilChannelDrainedAsync(
        TestableEventRaisedWorker worker,
        Channel<EventRaised> channel,
        int timeoutMs = 1000_000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        // Call ExecuteAsync directly — no StartAsync, no timer, no background thread
        var executeTask = worker.RunAsync(cts.Token);

        // Poll until the channel is empty
        while (channel.Reader.Count > 0 && !cts.IsCancellationRequested)
            await Task.Delay(100);

        // Small buffer for the final DB write to complete
        await Task.Delay(500);

        cts.Cancel();

        try { await executeTask; }
        catch (OperationCanceledException) { } // expected on cancellation
    }

    //private EventRaisedWorker CreateWorker() =>
    //    new EventRaisedWorker(
    //        _channel,
    //        _serviceProvider.GetRequiredService<IServiceScopeFactory>());

    private static EventRaised BuildEventRaised(Guid? id = null) =>
        new EventRaised ( createdEventId: id ?? Guid.NewGuid() );

    private static WebhookEvent BuildWebhookEvent(
        Guid?              id        = null,
        string             eventType = "CUSTOMERCREATED",
        WebHookEventStatus status    = WebHookEventStatus.Pending,
        string             payload   = "{}") => new()
    {
        Id        = id ?? Guid.NewGuid(),
        EventType = eventType,
        Status    = status,
        PayLoad   = payload,
        Source    = "CustomerService",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static WebHookEventCatalog BuildCatalog(List<string> subscribedFields, string eventName = "CustomerCreated") => new()
    {
        Id                  = Guid.NewGuid(),
        EventName           = eventName,
        NormalizedEventName = eventName.ToUpper(),
        IsActive            = true,
        CreatedAt         = DateTimeOffset.UtcNow,
        AvailableFields = subscribedFields.ToDictionary(f => f, f => "string")
    };

    private static WebhookSubscription BuildSubscription(
        string callbackUrl = "https://partner.com/webhook",
        bool   isActive    = true) => new()
    {
        Id          = Guid.NewGuid(),
        Name        = "Test Subscription",
        CallbackUrl = callbackUrl,
        IsActive    = isActive,
        CreatedAt   = DateTimeOffset.UtcNow,
        SecretKey = "test-secret-key"
    };

    /// <summary>
    /// Starts the worker and waits for at least one 5-second PeriodicTimer
    /// tick to fire, then cancels.
    /// </summary>
    private static async Task RunWorkerForOneTickAsync(
        EventRaisedWorker worker,
        int               waitMs = 6500)
    {
        using var cts = new CancellationTokenSource();
        _ = worker.StartAsync(cts.Token);
        await Task.Delay(waitMs);
        cts.Cancel();
        try { await worker.StopAsync(CancellationToken.None); }
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

        var stopTask        = worker.StopAsync(CancellationToken.None);
        var completedInTime = await Task.WhenAny(stopTask, Task.Delay(5000)) == stopTask;

        Assert.True(completedInTime, "StopAsync did not complete within 5 seconds.");
    }

    // -------------------------------------------------------------------------
    // FOR UPDATE SKIP LOCKED — real PostgreSQL behaviour
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_PendingEvent_StatusChangedToProcessing()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Pending);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

        Assert.NotNull(updated);
        Assert.Equal(WebHookEventStatus.Processing, updated!.Status);
        Assert.NotNull(updated.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_EventAlreadyProcessing_SkippedByForUpdateSkipLocked()
    {
        // Arrange — status is Processing, not Pending, so FOR UPDATE SKIP LOCKED
        // returns null and the worker rolls back and skips the item
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert — status and ProcessedAt must remain unchanged
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var unchanged         = await assertCtx.WebhookEvents.FindAsync(webhookEvent.Id);

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

    //[Fact]
    //public async Task ExecuteAsync_OneActiveSubscriber_OneDeliveryCreated()
    //{
    //    // Arrange
    //    using var scope  = _serviceProvider.CreateScope();
    //    var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
    //    var catalog      = BuildCatalog(["name", "email"] ,"CustomerCreated");
    //    var subscription = BuildSubscription("https://partner.com/webhook");
    //    var subEvent     = new WebhookSubscriptionEvent
    //    {
    //        Id                    = Guid.NewGuid(),
    //        WebhookSubscriptionId = subscription.Id,
    //        WebhookEventCatalogId = catalog.Id,
    //        IsActive              = true,
    //        CreatedAt             = DateTimeOffset.UtcNow
    //    };
    //    var webhookEvent = BuildWebhookEvent(
    //        eventType: "CUSTOMERCREATED",
    //        status:    WebHookEventStatus.Pending,
    //        payload:   @"{""customerId"":""123""}");

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
    //    var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
    //    var deliveries        = await assertCtx.WebhookDeliveries
    //        //.Where(d => d.WebhookEventId == webhookEvent.Id)
    //        .ToListAsync();

    //    Assert.Single(deliveries);
    //    Assert.Equal("https://partner.com/webhook", deliveries[0].CallBackUrl);
    //    Assert.Equal(WebhookDeliveryStatus.Pending, deliveries[0].DeliveryStatus);
    //    Assert.Equal(0,                             deliveries[0].RetryCount);
    //    Assert.Equal(webhookEvent.PayLoad,          deliveries[0].RequestPayload);
    //}

    [Fact]
    public async Task ExecuteAsync_RandomActiveSubscribers_ThreeDeliveriesCreated()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog      = BuildCatalog(["paymentreference", "amount"], "PaymentCompleted");
        var webhookEvent = BuildWebhookEvent(
            eventType: "PAYMENTCOMPLETED",
            status:    WebHookEventStatus.Pending);

        int recordCount = Random.Shared.Next(1, 5);

        List<WebhookSubscription> subscriptions = [];

        for (int i = 1; i <= recordCount; i++)
        {
            subscriptions.Add(BuildSubscription($"https://partner-{i}.com/webhook"));
        }

        //var subscriptions = new[]
        //{
        //    BuildSubscription("https://partner-a.com/webhook"),
        //    BuildSubscription("https://partner-b.com/webhook"),
        //    BuildSubscription("https://partner-c.com/webhook")
        //};

        var subEvents = subscriptions.Select(s => new WebhookSubscriptionEvent
        {
            Id                    = Guid.NewGuid(),
            WebhookSubscriptionId = s.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive              = true,
            CreatedAt             = DateTimeOffset.UtcNow
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
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries        = await assertCtx.WebhookDeliveries
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
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog      = BuildCatalog(["name", "accountnumber"], "AccountApproved");
        var subscription = BuildSubscription(isActive: false); // inactive
        var subEvent     = new WebhookSubscriptionEvent
        {
            Id                    = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive              = true,
            CreatedAt             = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "ACCOUNTAPPROVED",
            status:    WebHookEventStatus.Pending);

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
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries        = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Empty(deliveries);
    }

    [Fact]
    public async Task ExecuteAsync_NoSubscribers_NoDeliveryCreated()
    {
        // Arrange — valid event but no subscriptions exist
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var webhookEvent = BuildWebhookEvent(
            eventType: "ACCOUNTREJECTED",
            status:    WebHookEventStatus.Pending);

        await ctx.WebhookEvents.AddAsync(webhookEvent);
        await ctx.SaveChangesAsync();

        await _channel.Writer.WriteAsync(BuildEventRaised(webhookEvent.Id));

        // Act
        await RunWorkerForOneTickAsync(CreateWorker());

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries        = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Empty(deliveries);
    }

    [Fact]
    public async Task ExecuteAsync_DeliveryPayloadMatchesOriginalEventPayload()
    {
        // Arrange
        using var scope        = _serviceProvider.CreateScope();
        var ctx                = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        const string expected  = @"{""customerId"":""abc-123"",""firstName"":""John""}";
        var catalog            = BuildCatalog(["customerId", "firstName"], "CustomerUpdated");
        var subscription       = BuildSubscription();
        var subEvent           = new WebhookSubscriptionEvent
        {
            Id                    = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive              = true,
            CreatedAt             = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "CUSTOMERUPDATED",
            status:    WebHookEventStatus.Pending,
            payload:   expected);

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
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery          = await assertCtx.WebhookDeliveries
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
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var catalog      = BuildCatalog(["name", "email"], "CustomerCreated");
        var subscription = BuildSubscription();
        var subEvent     = new WebhookSubscriptionEvent
        {
            Id                    = Guid.NewGuid(),
            WebhookSubscriptionId = subscription.Id,
            WebhookEventCatalogId = catalog.Id,
            IsActive              = true,
            CreatedAt             = DateTimeOffset.UtcNow
        };
        var webhookEvent = BuildWebhookEvent(
            eventType: "CUSTOMERCREATED",
            status:    WebHookEventStatus.Pending);

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
        var workerA      = new EventRaisedWorker(channelA, scopeFactory);
        var workerB      = new EventRaisedWorker(channelB, scopeFactory);

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
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var deliveries        = await assertCtx.WebhookDeliveries
            .Where(d => d.WebhookEventId == webhookEvent.Id)
            .ToListAsync();

        Assert.Single(deliveries);
    }
}



// Only exists in the test project — never ships to production
internal sealed class TestableEventRaisedWorker : EventRaisedWorker
{
    public TestableEventRaisedWorker(
        Channel<EventRaised> channel,
        IServiceScopeFactory scopeFactory)
        : base(channel, scopeFactory) { }

    // Exposes the protected ExecuteAsync so tests can call it directly
    public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
}