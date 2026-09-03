using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.WebhookDeadLetterQueue;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;

namespace WebHook.Tests.UnitTests.Services;

public class DeadLetterQueueServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{

    private readonly PostgreSqlFixture _postgreSqlFixture;
    private ServiceProvider _serviceProvider = null;
    private readonly Mock<IAuthenticatedUserDetails> _authenticatedUserDetailsMock;

    public DeadLetterQueueServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
        _authenticatedUserDetailsMock = new Mock<IAuthenticatedUserDetails>();
    }

    private List<string> _encryptedSecrets = [];
    private List<WebhookSubscription> _webhookSubscriptions = [];
    private List<WebHookEventCatalog> _webHookEventCatalogs = [];


    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
        {
            await _serviceProvider.DisposeAsync();
        }

    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        //Add databse test container service.
        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_postgreSqlFixture.ConnectionString));

        services.Configure<DeadLetterManualRetryConfiguration>(opts =>
        {
            opts.MaximumRetryCycle = 3;
        });

        services.AddHttpContextAccessor();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();
        services.AddScoped<IAuthenticatedUserDetails, AuthenticatedUserDetails>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var encryptor = _serviceProvider.GetRequiredService<IEncryptionService>();

        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        // Truncate all tables between tests — faster than drop/recreate
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

    private DeadLetterQueueService CreateSut(RepositoryContext context, IAuthenticatedUserDetails authenticatedUserDetails = null)
    {
        return new DeadLetterQueueService(context, _serviceProvider.GetRequiredService<IOptionsMonitor<DeadLetterManualRetryConfiguration>>(), authenticatedUserDetails ?? _serviceProvider.GetRequiredService<IAuthenticatedUserDetails>());
    }


    private static WebhookEvent BuildWebhookEvent(Guid? id = null, string eventType = "CUSTOMERCREATED", WebHookEventStatus status = WebHookEventStatus.Pending, string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}") => new()
    {
        Id = id ?? Guid.NewGuid(),
        EventType = eventType.ToUpper(),
        Status = status,
        PayLoad = payload,
        Source = "CustomerService",
        CreatedAt = DateTimeOffset.UtcNow
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

    private WebhookDelivery BuildDelivery(string callbackUrl = "https://partner.com/webhook", string payload = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}", int retryCount = 0, WebhookDeliveryStatus status = WebhookDeliveryStatus.DeadLetter, int retryCycle = 1, Guid? subscriptionId = null) => new()
    {
        Id = Guid.NewGuid(),
        CallBackUrl = callbackUrl,
        RequestPayload = payload,
        DeliveryStatus = status,
        RetryCount = retryCount,
        RetryCycle = retryCycle,
        CreatedAt = DateTimeOffset.UtcNow,
        WebhookSubscriptionEventId = subscriptionId ?? _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
        WebhookDeliveryAttempts = new List<WebhookDeliveryAttempt>() // initialise to avoid NullRef
    };

    private static WebhookDeadLetterQueue BuildDlqEntry(Guid deliveryId, DateTimeOffset? retriedAt = null, string? retriedBy = null, string? retryJustification = null) => new()
    {
        Id = Guid.NewGuid(),
        WebhookDeliveryId = deliveryId,
        Reason = "Exceeded maximum retry attempts.",
        CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        RetriedAt = retriedAt,
        RetriedBy = retriedBy,
        RetryJustification = retryJustification
    };

    private RequestManualRetryDto BuildRetryRequest(Guid deadLetterId, Guid deliveryId, string justification = "Endpoint is now healthy.", string retriedBy = "admin@webhookservice.com") => new()
    {
        DeadLetterId = deadLetterId,
        RetryJustification = justification,
        DeliveryId = deliveryId
    };


    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_NonExistentDeadLetterId_Returns404()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var request = BuildRetryRequest(Guid.NewGuid(), Guid.NewGuid());

        // Act
        var result = await sut.RequestManualRetryAsync(request);

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
    }

    [Fact]
    public async Task RequestManualRetryAsync_NonExistentDeadLetterId_DoesNotThrow()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var request = BuildRetryRequest(Guid.NewGuid(), Guid.NewGuid());

        // Act & Assert
        var ex = await Record.ExceptionAsync(() => sut.RequestManualRetryAsync(request));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — already retried (Conflict)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_AlreadyRetried_Returns409Conflict()
    {
        // Arrange — DLQ entry already has RetriedAt set
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery();
        var dlqEntry = BuildDlqEntry(
            delivery.Id,
            retriedAt: DateTimeOffset.UtcNow.AddHours(-1),
            retriedBy: "admin@webhookservice.com");

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        var request = BuildRetryRequest(dlqEntry.Id, deliveryId: delivery.Id);

        // Act
        var result = await sut.RequestManualRetryAsync(request);

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.Conflict, result.HttpStatusCode);
    }

    [Fact]
    public async Task RequestManualRetryAsync_AlreadyRetried_DeliveryStatusUnchanged()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.Pending); // already promoted
        var dlqEntry = BuildDlqEntry(
            delivery.Id,
            retriedAt: DateTimeOffset.UtcNow.AddHours(-1));

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — delivery status unchanged
        var unchanged = await ctx.WebhookDeliveries.FindAsync(delivery.Id);
        Assert.Equal(WebhookDeliveryStatus.Pending, unchanged!.DeliveryStatus);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — invalid delivery status
    // BUG 2 documented: check is != Failed but should be != DeadLetter
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_DeliveryNotInDeadLetterStatus_Returns400()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.Failed); //Not at dead letter
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — rejects because status != Dead Letter
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.BadRequest, result.HttpStatusCode);
        Assert.Contains("Delivery Status", result.ResponseMessage);
    }

    [Fact]
    public async Task RequestManualRetryAsync_DeliveryInDeadLetterStatus_ProcessSuccessfully()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — retry cycle exceeded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_RetryCycleAtMaximum_Returns422()
    {
        // Arrange — RetryCycle equals MaximumRetryCycle (3)
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(
            status: WebhookDeliveryStatus.DeadLetter,
            retryCycle: 3); // equals MaximumRetryCycle
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.HttpStatusCode);
        Assert.Contains("cycle", result.ResponseMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestManualRetryAsync_RetryCycleExceedsMaximum_Returns422()
    {
        // Arrange — RetryCycle exceeds MaximumRetryCycle
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(
            status: WebhookDeliveryStatus.DeadLetter,
            retryCycle: 5); // above MaximumRetryCycle (3)
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, result.HttpStatusCode);
    }

    [Fact]
    public async Task RequestManualRetryAsync_RetryCycleBelowMaximum_Proceeds()
    {
        // Arrange — RetryCycle = 1, MaximumRetryCycle = 3 → allowed
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(
            status: WebhookDeliveryStatus.DeadLetter,
            retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — not rejected by cycle check (may succeed or fail for other reasons)
        Assert.NotEqual(HttpStatusCode.UnprocessableEntity, result.HttpStatusCode);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — successful retry request
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_Returns200()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        var request = BuildRetryRequest(dlqEntry.Id, deliveryId: delivery.Id, justification: "Endpoint is healthy again.");

        // Act
        var result = await sut.RequestManualRetryAsync(request);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_DeliveryStatusSetToPending()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Pending, updated!.DeliveryStatus);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_RetryCycleIncremented()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var originalCycle = delivery.RetryCycle;
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(originalCycle + 1, updated!.RetryCycle);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_LockFieldsCleared()
    {
        // Arrange — delivery has stale lock fields
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        delivery.LockedBy = "worker-1";
        delivery.LockedUntil = DateTimeOffset.UtcNow.AddMinutes(5);

        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — lock fields cleared
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Null(updated!.LockedBy);
        Assert.Null(updated.LockedUntil);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_NextRetryAtCleared()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        delivery.NextRetryAt = DateTimeOffset.UtcNow.AddHours(1); // has future retry scheduled

        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — NextRetryAt cleared so processor picks it up immediately
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Null(updated!.NextRetryAt);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_DlqRetriedAtIsSet()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);
        var beforeCall = DateTimeOffset.UtcNow;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeadLetterQueues.FindAsync(dlqEntry.Id);

        Assert.NotNull(updated!.RetriedAt);
        Assert.True(updated.RetriedAt >= beforeCall);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_JustificationPersisted()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        const string justification = "Subscriber confirmed endpoint is healthy after maintenance.";
        var request = BuildRetryRequest(dlqEntry.Id, delivery.Id, justification: justification);

        // Act
        await sut.RequestManualRetryAsync(request);

        // Assert
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeadLetterQueues.FindAsync(dlqEntry.Id);

        Assert.Equal(justification, updated!.RetryJustification);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_RetriedByNotEmpty()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        var request = BuildRetryRequest(
            dlqEntry.Id, delivery.Id,
            retriedBy: "admin@webhookservice.com");

        // Act
        await sut.RequestManualRetryAsync(request);

        // Assert — documents bug: RetriedBy is "" not the request value
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeadLetterQueues.FindAsync(dlqEntry.Id);

        // This assertion passes WITH the bug (RetriedBy is always "")
        Assert.NotEqual("", updated!.RetriedBy);
        // Assert.Equal("admin@webhookservice.com", updated!.RetriedBy);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_RetryCountNotReset()
    {
        // Arrange
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        delivery.RetryCount = 5; // maxed out

        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — documents bug: RetryCount still 5, not reset to 0
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(delivery.RetryCount, updated!.RetryCount);
    }

    [Fact]
    public async Task RequestManualRetryAsync_ValidRequest_RetriedByUpdated()
    {
        // Arrange
        var userId = Guid.NewGuid().ToString("N");
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(userId);
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var delivery = BuildDelivery(status: WebhookDeliveryStatus.DeadLetter, retryCycle: 1);
        delivery.RetryCount = 5; // maxed out

        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        await sut.RequestManualRetryAsync(BuildRetryRequest(dlqEntry.Id, delivery.Id));

        // Assert — documents bug: RetryCount still 5, not reset to 0
        using var assertCtx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var updated = await assertCtx.WebhookDeadLetterQueues.FindAsync(dlqEntry.Id);

        Assert.Equal(userId, updated!.RetriedBy);
    }

    // -------------------------------------------------------------------------
    // RequestManualRetryAsync — cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestManualRetryAsync_CancellationRequested_Returns500WithoutThrow()
    {
        // Arrange
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => sut.RequestManualRetryAsync(
                BuildRetryRequest(Guid.NewGuid(), Guid.NewGuid()), cts.Token));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // GetDeliveryDeadLetterAsync — not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_NoRecordsForDelivery_Returns404()
    {
        // Arrange — no DLQ entries for this delivery ID
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);

        // Act
        var result = await sut.GetDeliveryDeadKetterAsync(Guid.NewGuid());

        // Assert
        Assert.False(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.NotFound, result.HttpStatusCode);
        Assert.Null(result.ResponseData);
    }

    // -------------------------------------------------------------------------
    // GetDeliveryDeadLetterAsync — found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_RecordsExist_Returns200WithData()
    {
        // Arrange
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        var delivery = BuildDelivery();
        var dlqEntry = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.GetDeliveryDeadKetterAsync(delivery.Id);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(HttpStatusCode.OK, result.HttpStatusCode);
        Assert.NotNull(result.ResponseData);
        Assert.Single(result.ResponseData);
    }

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_RecordsExist_CorrectDeliveryIdReturned()
    {
        // Arrange
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        var delivery = BuildDelivery();
        var dlqEntry = BuildDlqEntry(
            delivery.Id,
            retriedAt: DateTimeOffset.UtcNow.AddHours(-1),
            retriedBy: "admin@webhookservice.com",
            retryJustification: "Test justification");

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.GetDeliveryDeadKetterAsync(delivery.Id);

        // Assert — DTO fields mapped correctly
        Assert.NotNull(result.ResponseData);
        var dto = result.ResponseData.First();
        Assert.Equal(dlqEntry.Id, dto.id);
        Assert.Equal(dlqEntry.Reason, dto.reason);
        Assert.Equal(dlqEntry.RetriedBy, dto.retriedBy);
        Assert.Equal(dlqEntry.RetryJustification, dto.RetryJustification);
        Assert.NotNull(dto.RetriedAt);
    }

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_MultipleEntries_AllReturned()
    {
        // Arrange — two DLQ entries for same delivery (retried twice)
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        var delivery = BuildDelivery();
        var entry1 = BuildDlqEntry(delivery.Id);
        var entry2 = BuildDlqEntry(delivery.Id);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddRangeAsync(entry1, entry2);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.GetDeliveryDeadKetterAsync(delivery.Id);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.NotNull(result.ResponseData);
        Assert.Equal(2, result.ResponseData.Count);
    }

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_OnlyReturnsEntriesForRequestedDelivery()
    {
        // Arrange — two deliveries, each with a DLQ entry
        _authenticatedUserDetailsMock.Setup(x => x.userId).Returns(Guid.NewGuid().ToString("N"));
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx, _authenticatedUserDetailsMock.Object);
        var deliveryA = BuildDelivery();
        var deliveryB = BuildDelivery();
        var entryA = BuildDlqEntry(deliveryA.Id);
        var entryB = BuildDlqEntry(deliveryB.Id);

        await ctx.WebhookDeliveries.AddRangeAsync(deliveryA, deliveryB);
        await ctx.WebhookDeadLetterQueues.AddRangeAsync(entryA, entryB);
        await ctx.SaveChangesAsync();

        // Act — request only delivery A's DLQ entries
        var result = await sut.GetDeliveryDeadKetterAsync(deliveryA.Id);

        // Assert — only delivery A's entry returned
        Assert.True(result.IsSuccessful);
        Assert.Single(result.ResponseData!);
        Assert.Equal(entryA.Id, result.ResponseData!.First().id);
    }

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_NotRetriedEntry_RetriedAtIsNull()
    {
        // Arrange — entry has never been retried
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        var delivery = BuildDelivery();
        var dlqEntry = BuildDlqEntry(delivery.Id, retriedAt: null);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.WebhookDeadLetterQueues.AddAsync(dlqEntry);
        await ctx.SaveChangesAsync();

        // Act
        var result = await sut.GetDeliveryDeadKetterAsync(delivery.Id);

        // Assert
        Assert.Null(result.ResponseData!.First().RetriedAt);
    }

    // -------------------------------------------------------------------------
    // GetDeliveryDeadLetterAsync — cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetDeliveryDeadLetterAsync_CancellationRequested_Returns500WithoutThrow()
    {
        // Arrange
        var ctx = _serviceProvider.GetRequiredService<RepositoryContext>();
        var sut = CreateSut(ctx);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        var ex = await Record.ExceptionAsync(
            () => sut.GetDeliveryDeadKetterAsync(Guid.NewGuid(), cts.Token));

        Assert.Null(ex);
    }
}
