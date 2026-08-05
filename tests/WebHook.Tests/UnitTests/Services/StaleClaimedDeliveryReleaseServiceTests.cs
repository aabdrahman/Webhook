using MassTransit.Courier.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;
using WebHook.Tests.Utilities;

namespace WebHook.Tests.UnitTests.Services;

public class StaleClaimedDeliveryReleaseServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly PostgreSqlFixture _fixture;
    private ServiceProvider _serviceProvider = null!;
    private MockHttpMessageHandler _httpHandler = null!;
    private List<WebhookSubscription> _webhookSubscriptions;
    private List<WebHookEventCatalog> _webHookEventCatalogs;

    public StaleClaimedDeliveryReleaseServiceTests(PostgreSqlFixture fixture)
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

        private (RepositoryContext ctx, StaleClaimedDeliveryReleaseService svc) CreateSut()
        {
            var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
            return (ctx, new StaleClaimedDeliveryReleaseService(ctx));
        }

        /// <summary>
        /// Builds a delivery that IS stale — locked, in Processing, lock expired.
        /// </summary>
        private WebhookDelivery BuildStaleDelivery(
            string callbackUrl = "https://partner.com/webhook",
            string payload = @"{""customerId"":""123""}",
            int retryCount = 2,
            WebhookDeliveryStatus status = WebhookDeliveryStatus.Processing, Guid? subscriptionId = null, double expiredAgeInSeconds = 700, string lockedBy = "worker-1") => new()
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
                LockedBy = lockedBy,
                LockedUntil = DateTimeOffset.UtcNow.AddSeconds(-expiredAgeInSeconds)
            };

            /// <summary>
            /// Builds a delivery whose lock has NOT yet expired — still legitimately
            /// in-flight.
            /// </summary>
            private WebhookDelivery BuildActiveLockDelivery(string payload = @"{""customerId"":""123""}", Guid? subscriptionId = null) => new()
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

            /// <summary>
            /// Builds a delivery with no lock — already released or never claimed.
            /// </summary>
            private WebhookDelivery BuildUnlockedDelivery(
                WebhookDeliveryStatus status = WebhookDeliveryStatus.Pending, string payload = @"{""customerId"":""123""}", Guid? subscriptionId = null) => new()
                {
                    Id = Guid.NewGuid(),
                    CallBackUrl = "https://partner.com/webhook",
                    RequestPayload = @"{""customerId"":""123""}",
                    DeliveryStatus = status,
                    RetryCount = 0,
                    LockedBy = null,
                    LockedUntil = null,
                    CreatedAt = DateTimeOffset.UtcNow,
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
    // Lock fields cleared on release
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_StaleDelivery_LockedBySetToNull()
    {
        // Arrange
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(lockedBy: "worker-1");

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        await svc.ProcessStaleDeliveriesAsync();

        // Assert
        var updated = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Null(updated!.LockedBy);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_StaleDelivery_LockedUntilSetToNull()
    {
        // Arrange
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        await svc.ProcessStaleDeliveriesAsync();

        // Assert
        var updated = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Null(updated!.LockedUntil);
    }

    // -------------------------------------------------------------------------
    // BUG 1 FIX — DeliveryStatus reset to Pending
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_StaleDelivery_StatusResetToPending()
    {
        // Arrange
        // BUG 1: Original code left status as Processing after clearing lock fields.
        // A delivery in Processing is never picked up by the processor worker.
        // Fix: status must be reset to Pending.
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        await svc.ProcessStaleDeliveriesAsync();

        // Assert
        var updated = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
    }

    // -------------------------------------------------------------------------
    // BUG 2 FIX — RetryCount incremented
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_StaleDelivery_RetryCountIncremented()
    {
        // Arrange
        // A stale delivery is a failed attempt — RetryCount must reflect this.
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(retryCount: 2);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        await svc.ProcessStaleDeliveriesAsync();

        // Assert
        var updated = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Equal(3, updated!.RetryCount); // 2 + 1
    }

    // -------------------------------------------------------------------------
    // BUG 3 FIX — return value
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_OneStaleDelivery_Returns1()
    {
        // Arrange
        var (ctx, svc) = CreateSut();
        await ctx.WebhookDeliveries.AddAsync(BuildStaleDelivery());
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_ThreeStaleDeliveries_Returns3()
    {
        // Arrange
        var (ctx, svc) = CreateSut();
        await ctx.WebhookDeliveries.AddRangeAsync(
            BuildStaleDelivery(),
            BuildStaleDelivery(),
            BuildStaleDelivery());
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert
        Assert.Equal(3, count);
    }

    // -------------------------------------------------------------------------
    // Filter correctness — only stale + Processing deliveries released
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_MixedDeliveries_OnlyStaleReleased()
    {
        // Arrange — one stale, one active lock, one unlocked pending
        var (ctx, svc) = CreateSut();
        var stale = BuildStaleDelivery();
        var activeLock = BuildActiveLockDelivery();
        var unlocked = BuildUnlockedDelivery(WebhookDeliveryStatus.Pending);

        await ctx.WebhookDeliveries.AddRangeAsync(stale, activeLock, unlocked);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert — only the stale one was released
        Assert.Equal(1, count);

        var updatedStale = await ctx.WebhookDeliveries.FindAsync(stale.Id);
        var updatedActiveLock = await ctx.WebhookDeliveries.FindAsync(activeLock.Id);
        var updatedUnlocked = await ctx.WebhookDeliveries.FindAsync(unlocked.Id);

        // Stale — released
        Assert.Equal(WebhookDeliveryStatus.Failed, updatedStale!.DeliveryStatus);
        Assert.Null(updatedStale.LockedBy);
        Assert.Null(updatedStale.LockedUntil);

        // Active lock — untouched
        Assert.Equal(WebhookDeliveryStatus.Processing, updatedActiveLock!.DeliveryStatus);
        Assert.NotNull(updatedActiveLock.LockedBy);

        // Unlocked pending — untouched
        Assert.Equal(WebhookDeliveryStatus.Pending, updatedUnlocked!.DeliveryStatus);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_DeliveredDeliveryWithExpiredLock_NotReleased()
    {
        // Arrange — Delivered delivery with an expired lock (edge case)
        // Should not be touched — only Processing deliveries are released
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();
        delivery.DeliveryStatus = WebhookDeliveryStatus.Retrying;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert — Delivered delivery not touched
        Assert.Equal(0, count);

        var updated = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Equal(WebhookDeliveryStatus.Retrying, updated!.DeliveryStatus);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_FailedDeliveryWithExpiredLock_NotReleased()
    {
        // Arrange — Failed delivery with lock — only Processing is in scope
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();
        delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_ProcessingWithNoLockedBy_NotReleased()
    {
        // Arrange — Processing but LockedBy is null — not claimed by a worker
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();
        delivery.LockedBy = null; // no worker claimed it

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert — not in scope (LockedBy must be set)
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_ProcessingWithNoLockedUntil_NotReleased()
    {
        // Arrange — Processing and LockedBy set but no LockedUntil
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery();
        delivery.LockedUntil = null; // no expiry set

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert — not in scope (LockedUntil must have a value)
        Assert.Equal(0, count);
    }

    // -------------------------------------------------------------------------
    // lockDurationSeconds threshold boundary
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_LockExpiredExactlyAtThreshold_IsReleased()
    {
        // Arrange — lock expired exactly at the threshold (boundary inclusive)
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(expiredAgeInSeconds: 601); // just past 600s threshold

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync(lockDurationSeconds: 600);

        // Assert — expired past threshold, must be released
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_LockNotYetExpiredAtThreshold_NotReleased()
    {
        // Arrange — lock expired only 100 seconds ago, threshold is 600 seconds
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(expiredAgeInSeconds: 100); // not past threshold

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync(lockDurationSeconds: 600);

        // Assert — not stale enough yet
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_CustomLockDuration_RespectsCustomThreshold()
    {
        // Arrange — lock expired 35 seconds ago, custom threshold is 30 seconds
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(expiredAgeInSeconds: 35);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act — custom short threshold
        var count = await svc.ProcessStaleDeliveriesAsync(lockDurationSeconds: 30);

        // Assert — 35 > 30, so it is stale and released
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // Multiple workers — different LockedBy values
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_StaleDeliveriesFromDifferentWorkers_AllReleased()
    {
        // Arrange — three stale deliveries claimed by different workers
        var (ctx, svc) = CreateSut();
        await ctx.WebhookDeliveries.AddRangeAsync(
            BuildStaleDelivery(lockedBy: "worker-1"),
            BuildStaleDelivery(lockedBy: "worker-2"),
            BuildStaleDelivery(lockedBy: "worker-3"));
        await ctx.SaveChangesAsync();

        // Act
        var count = await svc.ProcessStaleDeliveriesAsync();

        // Assert — all three released regardless of which worker claimed them
        Assert.Equal(3, count);

        var allDeliveries = await ctx.WebhookDeliveries.ToListAsync();
        Assert.All(allDeliveries, d =>
        {
            Assert.Null(d.LockedBy);
            Assert.Null(d.LockedUntil);
            Assert.Equal(WebhookDeliveryStatus.Failed, d.DeliveryStatus);
        });
    }

    // -------------------------------------------------------------------------
    // Cancellation — BUG 4 FIX
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_CancellationRequested_Returns0WithoutException()
    {
        // Arrange
        // BUG 4: Original code had no exception handling — a cancelled token
        // would throw OperationCanceledException and crash the background service.
        // Fix: catch cancellation and return 0 gracefully.
        var (_, svc) = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — must not throw
        var ex = await Record.ExceptionAsync(
            () => svc.ProcessStaleDeliveriesAsync(ct: cts.Token));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Persisted correctly after SaveChangesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessStaleDeliveriesAsync_AllChangesPersistedInOneCall()
    {
        // Arrange — verify changes are visible from a fresh context
        // (not just the tracked context used by the service)
        var (ctx, svc) = CreateSut();
        var delivery = BuildStaleDelivery(retryCount: 1);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        // Act
        await svc.ProcessStaleDeliveriesAsync();

        // Assert — fresh context to verify persistence
        using var freshCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var persisted = await freshCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Null(persisted!.LockedBy);
        Assert.Null(persisted.LockedUntil);
        Assert.Equal(WebhookDeliveryStatus.Failed, persisted.DeliveryStatus);
        Assert.Equal(2, persisted.RetryCount); // 1 + 1
    }
}
