using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Net;
using System.Threading.Channels;
using Testcontainers.PostgreSql;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;
using WebHook.IntegrationTests.BackgroundWorkers;
using Xunit;

namespace WebHook.IntegrationTests.Services;

/// <summary>
/// Integration tests for <see cref="WebhookDeliveryProcessorService"/>.
///
/// Uses:
///   - Testcontainers PostgreSQL — real DB so FromSqlRaw + FOR UPDATE SKIP LOCKED works
///   - MockHttpMessageHandler — controls HTTP responses without real network calls
///   - WebhookDeliveryRetryAfterService — called directly (concrete, no interface)
/// Prerequisites:
///   - Docker running
///   - Testcontainers.PostgreSql 4.12.0
///   - Npgsql.EntityFrameworkCore.PostgreSQL (same version as main project)
/// </summary>
public sealed class WebhookDeliveryProcessorServiceTests
    : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    // -------------------------------------------------------------------------
    // Fields
    // -------------------------------------------------------------------------

    private readonly PostgreSqlFixture _fixture;
    private ServiceProvider _serviceProvider = null!;
    private List<WebhookSubscription> _webhookSubscriptions;
    private List<WebHookEventCatalog> _webHookEventCatalogs;
    private List<string> _encryptedSecrets = [];

    public WebhookDeliveryProcessorServiceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    // -------------------------------------------------------------------------
    // IAsyncLifetime — fresh schema per test
    // -------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        //Add databse test container service.
        services.AddDbContext<RepositoryContext>(opt =>
            opt.UseNpgsql(_fixture.ConnectionString));

        //Add the signature and encrptor services.
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddScoped<ISignatureService, SignatureService>();

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var encryptor = _serviceProvider.GetRequiredService<IEncryptionService>();

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

    public async Task DisposeAsync() =>
        await _serviceProvider.DisposeAsync();

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the service under test with a controllable HTTP handler.
    /// The <paramref name="httpHandler"/> controls what the mock consumer
    /// endpoint returns for every POST request.
    /// </summary>
    private WebhookDeliveryProcessorService CreateSut(
        MockHttpMessageHandler httpHandler,
        RepositoryContext      ctx)
    {
        var httpClientFactory = new MockHttpClientFactory(httpHandler);
        var retryAfterService = new WebhookDeliveryRetryAfterService();
        return new WebhookDeliveryProcessorService(ctx, httpClientFactory, retryAfterService, _serviceProvider.GetRequiredService<ISignatureService>(), _serviceProvider.GetRequiredService<IEncryptionService>());
    }

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

    //private static List<WebhookSubscription> WebhookSubscriptions = new List<WebhookSubscription>()
    //{
    //    BuildEntity("Subscription A", eventIds: webhookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList()),
    //    BuildEntity("Subscription B", eventIds: webhookEventCatalogs.OrderBy(x => x.Id).Select(x => x.Id).ToList())
    //};

    //private static List<WebHookEventCatalog> webhookEventCatalogs = new List<WebHookEventCatalog>()
    //{
    //    BuildCatalogEntity(new List<string>() { "customerId", "customerName" }, "CustomerCreated"),
    //    BuildCatalogEntity(new List<string>() { "orderId", "orderAmount" }, "OrderPlaced"),
    //    BuildCatalogEntity(new List<string>() { "paymentId", "paymentStatus" }, "PaymentProcessed"),
    //    BuildCatalogEntity(new List<string>() { "shipmentId", "shipmentStatus" }, "ShipmentDispatched"),
    //};

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

    private WebhookDelivery BuildPendingDelivery(
        string callbackUrl = "https://partner.com/webhook",
        string payload     = "{\"customerId\":\"12345\", \"customerName\":\"John Doe\"}",
        int    retryCount  = 0, Guid? subscriptionId = null) => new()
    {
        Id                        = Guid.NewGuid(),
        CallBackUrl               = callbackUrl,
        RequestPayload            = payload,
        DeliveryStatus            = WebhookDeliveryStatus.Pending,
        RetryCount                = retryCount,
        CreatedAt                 = DateTimeOffset.UtcNow,
        WebhookSubscriptionEventId = subscriptionId ?? _webhookSubscriptions.SelectMany(x => x.WebhookEvents).Select(x => x.Id).First(),
        webhookEvent = BuildWebhookEvent(status: WebHookEventStatus.Processing, payload: payload),
        WebhookDeliveryAttempts   = new List<WebhookDeliveryAttempt>() // initialise to avoid NullRef
    };

    // -------------------------------------------------------------------------
    // No pending deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_NoPendingDeliveries_ReturnsWithoutError()
    {
        // Arrange — empty database
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var handler     = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut         = CreateSut(handler, ctx);

        // Act & Assert — should return cleanly without throwing
        var ex = await Record.ExceptionAsync(
            () => sut.ProcessPendingDeliveriesAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_NoPendingDeliveries_NoHttpCallMade()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var handler     = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut         = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert — no HTTP requests were fired
        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Successful delivery — 2xx response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_SuccessfulResponse_StatusChangedToDelivered()
    {
        // Arrange
        using var scope  = _serviceProvider.CreateScope();
        var ctx          = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery     = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updated!.DeliveryStatus);
        Assert.NotNull(updated.DeliveredAt);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_SuccessfulResponse_DeliveredAtIsSet()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();
        var beforeCall  = DateTimeOffset.UtcNow;

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert — DeliveredAt is set and is after the call started
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.DeliveredAt);
        Assert.True(updated.DeliveredAt >= beforeCall);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_SuccessfulResponse_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: "OK");
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert — one attempt record created with correct HTTP response code
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts          = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal(StatusCodes.Status200OK.ToString(), attempts[0].HttpResponseCode);
        Assert.Equal(1,    attempts[0].AttemptedCount);
        Assert.True(attempts[0].Duration >= 0);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_201Response_StatusChangedToDelivered()
    {
        // Arrange — any 2xx is a success
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.Created);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, updated!.DeliveryStatus);
    }

    // -------------------------------------------------------------------------
    // Failed delivery — non-2xx response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_500Response_StatusChangedToFailed()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
        Assert.Null(updated.DeliveredAt);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_500Response_NextRetryAtIsSet()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery(retryCount: 0);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert — NextRetryAt is set in the future
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.NotNull(updated!.NextRetryAt);
        Assert.True(updated.NextRetryAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_404Response_StatusChangedToFailed()
    {
        // Arrange — any non-2xx is a failure
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.NotFound);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.FindAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, updated!.DeliveryStatus);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_FailedResponse_AttemptRecordCreated()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(
            HttpStatusCode.InternalServerError,
            responseBody: "Internal Server Error");
        var sut = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var attempts          = await assertCtx.WebhookDeliveryAttempts
            .Where(a => a.WebhookDeliveryId == delivery.Id)
            .ToListAsync();

        Assert.Single(attempts);
        Assert.Equal(StatusCodes.Status500InternalServerError.ToString(), attempts[0].HttpResponseCode);
    }

    // -------------------------------------------------------------------------
    // Multiple deliveries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_MultipleDeliveries_AllProcessed()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var deliveries = new[]
        {
            BuildPendingDelivery("https://partner-a.com/webhook"),
            BuildPendingDelivery("https://partner-b.com/webhook"),
            BuildPendingDelivery("https://partner-c.com/webhook")
        };

        await ctx.WebhookDeliveries.AddRangeAsync(deliveries);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync(totalToProcess: 10);

        // Assert — all three are delivered and HTTP was called three times
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var updated           = await assertCtx.WebhookDeliveries.ToListAsync();

        Assert.Equal(3, handler.CallCount);
        Assert.All(updated, d => Assert.Equal(WebhookDeliveryStatus.Delivered, d.DeliveryStatus));
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_TotalToProcessLimit_OnlyProcessesUpToLimit()
    {
        // Arrange — 5 pending deliveries but limit is 3
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        for (int i = 0; i < 5; i++)
            await ctx.WebhookDeliveries.AddAsync(
                BuildPendingDelivery($"https://partner-{i}.com/webhook"));

        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync(totalToProcess: 3);

        // Assert — only 3 HTTP calls made, 2 deliveries still pending
        Assert.Equal(3, handler.CallCount);

        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var delivered = await assertCtx.WebhookDeliveries
            .CountAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Delivered);
        var pending = await assertCtx.WebhookDeliveries
            .CountAsync(d => d.DeliveryStatus == WebhookDeliveryStatus.Pending);

        Assert.Equal(3, delivered);
        Assert.Equal(2, pending);
    }

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_MixedResponses_EachDeliveryUpdatedCorrectly()
    {
        // Arrange — two deliveries going to different URLs with different outcomes
        using var scope    = _serviceProvider.CreateScope();
        var ctx            = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var successDelivery = BuildPendingDelivery("https://success.com/webhook");
        var failDelivery    = BuildPendingDelivery("https://fail.com/webhook");

        await ctx.WebhookDeliveries.AddRangeAsync(successDelivery, failDelivery);
        await ctx.SaveChangesAsync();

        // Handler returns 200 for success URL, 500 for fail URL
        var handler = new MockHttpMessageHandler(responses: new Dictionary<string, HttpStatusCode>
        {
            { "https://success.com/webhook", HttpStatusCode.OK                  },
            { "https://fail.com/webhook",    HttpStatusCode.InternalServerError }
        });
        var sut = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx         = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var success = await assertCtx.WebhookDeliveries.FindAsync(successDelivery.Id);
        var fail    = await assertCtx.WebhookDeliveries.FindAsync(failDelivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Delivered, success!.DeliveryStatus);
        Assert.Equal(WebhookDeliveryStatus.Failed,    fail!.DeliveryStatus);
        Assert.NotNull(success.DeliveredAt);
        Assert.Null(fail.DeliveredAt);
        Assert.NotNull(fail.NextRetryAt);
    }

    // -------------------------------------------------------------------------
    // Only processes Pending — skips other statuses
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_AlreadyDeliveredRecord_NotReprocessed()
    {
        // Arrange — delivery is already Delivered, not Pending
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var delivery    = BuildPendingDelivery();
        delivery.DeliveryStatus = WebhookDeliveryStatus.Delivered;
        delivery.DeliveredAt    = DateTimeOffset.UtcNow.AddMinutes(-1);

        await ctx.WebhookDeliveries.AddAsync(delivery);
        await ctx.SaveChangesAsync();

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut     = CreateSut(handler, ctx);

        // Act
        await sut.ProcessPendingDeliveriesAsync();

        // Assert — no HTTP call made, status unchanged
        Assert.Equal(0, handler.CallCount);
    }

    // -------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProcessPendingDeliveriesAsync_CancellationRequested_ReturnsWithoutException()
    {
        // Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx         = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var handler     = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut         = CreateSut(handler, ctx);

        using var cts   = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert — should not throw
        var ex = await Record.ExceptionAsync(
            () => sut.ProcessPendingDeliveriesAsync(ct: cts.Token));

        Assert.Null(ex);
    }
}

// =============================================================================
// Test doubles
// =============================================================================

/// <summary>
/// A controllable <see cref="HttpMessageHandler"/> for use in tests.
/// Supports a fixed status code for all requests, or per-URL status codes
/// via the <paramref name="responses"/> dictionary constructor.
/// Tracks how many times it was called via <see cref="CallCount"/>.
/// </summary>
public sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode? _fixedStatusCode;
    private readonly string _responseBody;
    private readonly Dictionary<string, HttpStatusCode>? _urlResponses;

    public int CallCount { get; private set; }

    /// <summary>Fixed status code returned for every request.</summary>
    public MockHttpMessageHandler(
        HttpStatusCode statusCode,
        string         responseBody = "")
    {
        _fixedStatusCode = statusCode;
        _responseBody    = responseBody;
    }

    /// <summary>Per-URL status codes — key is the base URL of the request.</summary>
    public MockHttpMessageHandler(Dictionary<string, HttpStatusCode> responses)
    {
        _urlResponses = responses;
        _responseBody = "";
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage  request,
        CancellationToken   cancellationToken)
    {
        CallCount++;

        HttpStatusCode statusCode;

        if (_urlResponses is not null)
        {
            // Match on the base URL (scheme + host)
            var baseUrl = $"{request.RequestUri!.Scheme}://{request.RequestUri.Host}/webhook";
            statusCode  = _urlResponses.TryGetValue(baseUrl, out var code)
                ? code
                : HttpStatusCode.OK;
        }
        else
        {
            statusCode = _fixedStatusCode!.Value;
        }

        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(_responseBody)
        });
    }
}

/// <summary>
/// A minimal <see cref="IHttpClientFactory"/> that always returns an
/// <see cref="HttpClient"/> backed by the provided <see cref="MockHttpMessageHandler"/>.
/// </summary>
public sealed class MockHttpClientFactory : IHttpClientFactory
{
    private readonly MockHttpMessageHandler _handler;

    public MockHttpClientFactory(MockHttpMessageHandler handler) =>
        _handler = handler;

    public HttpClient CreateClient(string name = "") =>
        new HttpClient(_handler);
}
