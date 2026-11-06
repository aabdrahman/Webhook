using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.DataTransferObjects.EmailSender;
using WebHook.Core.DataTransferObjects.SubscriptionVerification;
using WebHook.Core.Entities;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.EventContracts.Publishers;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.EventPublishers;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.IntegrationTests.BackgroundWorkers;
using WebHook.Tests.Utilities;

namespace WebHook.Tests.UnitTests.Services;

public sealed class SubscriptionVerificationProcessorServiceTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgreSqlFixture;

    private List<WebhookSubscription> _webhookSubscriptions;
    private List<WebHookEventCatalog> _webHookEventCatalogs;
    private List<string> _encryptedSecrets = [];
    private ServiceProvider _serviceProvider;
    public SubscriptionVerificationProcessorServiceTests(PostgreSqlFixture postgreSqlFixture)
    {
        _postgreSqlFixture = postgreSqlFixture;
    }
    public async Task DisposeAsync()
    {
        if (_serviceProvider is not null)
            await _serviceProvider.DisposeAsync();
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();

        services.AddScoped<ISignatureService, SignatureService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.AddDbContext<RepositoryContext>(opts =>
        {
            opts.UseNpgsql(_postgreSqlFixture.ConnectionString);
        });

        services.AddScoped<SubscriptionVerificationService>();

        Environment.SetEnvironmentVariable("env_webhook_encrypt_key", "00AF7F388E47B0AC211E931842DAFA7422648881071244DF484725D249D9E5F1");
        Environment.SetEnvironmentVariable("env_webhook_encrypt_iv", "315BDF36FBC81E00D668494E187AC4A5");

        //Channel
        services.AddSingleton(_ =>
        {
            return Channel.CreateUnbounded<SubscriptionVerificationRaised>(new UnboundedChannelOptions()
            {

            });
        });

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
        //await ctx.Database.EnsureCreatedAsync();
        await ctx.Database.MigrateAsync();

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

    //Utility
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
        CreatedAt = DateTimeOffset.UtcNow,
        CorrelationId = Guid.CreateVersion7(DateTimeOffset.UtcNow)
    };

    private WebhookSubscription BuildEntity(string entityName, List<Guid> eventIds, string url = "https://example.com/", SubscriptionOnboardStatus onboardStatus = SubscriptionOnboardStatus.Active)
    {
        var entityId = Guid.NewGuid();

        return new WebhookSubscription()
        {
            Id = entityId,
            Name = entityName,
            IsActive = true,
            SubscribedFields = [],
            CallbackUrl = url,
            OnboardStatus = onboardStatus,
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

    private static WebhookSubscriptionVerification BuildSubscriptionVerificationEntity(Guid subscriptionId, string channelgeToken =  null) => new WebhookSubscriptionVerification()
    {
        SubscriptionId = subscriptionId,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = WebhookSubscriptionVerificationStatus.Pending,
        ChallengeToken = channelgeToken ?? Random.Shared.GetHexString(32)
    };

    private SubscriptionVerificationProcessorService CreateSut(RepositoryContext ctx, MockHttpMessageHandler mockHttpMessageHandler)
    {
        var httpClient = new MockHttpClientFactory(mockHttpMessageHandler);
        return new SubscriptionVerificationProcessorService(repositoryContext: ctx, httpClientFactory: httpClient, signatureService: _serviceProvider.GetRequiredService<ISignatureService>(), encryptionService: _serviceProvider.GetRequiredService<IEncryptionService>());
    }

    //[Fact]
    public async Task ProcessSignatureVerificationAsync_CancellationRequested_ThrowsException()
    {
        //Arrange

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var httpHandler = new MockHttpMessageHandler(statusCode: HttpStatusCode.OK);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var sut = CreateSut(ctx, httpHandler);

        //Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await sut.ProcessSignatureVerificationAsync(Guid.NewGuid(), ct: cts.Token));
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_InvalidVerificationId_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);

        var sut = CreateSut(ctx, httpHandler);
        var id = Guid.NewGuid();

        //AcT
        await sut.ProcessSignatureVerificationAsync(id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var verifiationRecordExists = await assertCtx.WebhookSubscriptionVerifications.AnyAsync(x => x.Id == id);
        Assert.False(verifiationRecordExists);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionActive_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id]);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));
        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);
        
        Assert.NotNull(assertVerificationRecord);
        Assert.Null(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationFailed_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id]);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));
        verificationRecord.Status = WebhookSubscriptionVerificationStatus.Failed;
        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.Null(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationHttpFailed_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id], onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));

        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError);
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.NotNull(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, assertVerificationRecord.Status);
        Assert.Equal(((int)HttpStatusCode.InternalServerError).ToString(), assertVerificationRecord.ResponseStatusCode);
        Assert.NotNull(assertVerificationRecord.ResponseDetails);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationHttpThrowsException_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id], onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));

        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var httpHandler = new MockHttpMessageHandler(exceptionToThrow: new HttpRequestException("The remote name could not be resolved"));
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.NotNull(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, assertVerificationRecord.Status);
        Assert.Equal("500", assertVerificationRecord.ResponseStatusCode);
        Assert.NotNull(assertVerificationRecord.ResponseDetails);
        Assert.Equal("An error occurred while sending the verification request.", assertVerificationRecord.ResponseDetails, ignoreCase: true);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationHttpSuccessContextInvalid_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id], onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));

        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: "");
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.NotNull(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, assertVerificationRecord.Status);
        Assert.NotNull(assertVerificationRecord.ResponseDetails);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationHttpSuccessContextValid_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var signature = scope.ServiceProvider.GetRequiredService<ISignatureService>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id], onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));

        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var signedChallenge = signature.GenerateSignature(verificationRecord.ChallengeToken, encryptionKey: encryptor.Decrypt(subscription.SecretKey));

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: JsonSerializer.Serialize(new SignatureVerificationResponseDto(Challenge: verificationRecord.ChallengeToken)));
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.NotNull(assertVerificationRecord.AttemptedAt);
        Assert.NotNull(assertVerificationRecord.ValidatedAt);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, assertVerificationRecord.Status);
        Assert.NotNull(assertVerificationRecord.ResponseDetails);
        Assert.Equal(((int)HttpStatusCode.OK).ToString(), assertVerificationRecord.ResponseStatusCode);
        Assert.Equal(SubscriptionOnboardStatus.Active, assertVerificationRecord.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_SubscriptionVerificationHttpSuccessContentValidInvalidChallegeReturned_RunsSuccessfully()
    {
        //Arrange
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var signature = scope.ServiceProvider.GetRequiredService<ISignatureService>();
        var encryptor = scope.ServiceProvider.GetRequiredService<IEncryptionService>();

        var subId = _webhookSubscriptions.First().Id;
        var subscription = BuildEntity("Subscription Test", eventIds: [_webHookEventCatalogs.First().Id], onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verificationRecord = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id, channelgeToken: Random.Shared.GetHexString(32));

        subscription.WebhookSubscriptionVerifications.Add(verificationRecord);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        var signedChallenge = signature.GenerateSignature(verificationRecord.ChallengeToken, encryptionKey: encryptor.Decrypt(subscription.SecretKey));

        var httpHandler = new MockHttpMessageHandler(HttpStatusCode.OK, responseBody: JsonSerializer.Serialize(new SignatureVerificationResponseDto(Challenge: Random.Shared.GetHexString(32))));
        var sut = CreateSut(ctx, httpHandler);

        //Act
        await sut.ProcessSignatureVerificationAsync(verificationId: verificationRecord.Id, CancellationToken.None);

        //Assert
        using var assertScope = _serviceProvider.CreateScope();
        var assertCtx = assertScope.ServiceProvider.GetRequiredService<RepositoryContext>();

        WebhookSubscriptionVerification? assertVerificationRecord = await assertCtx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstOrDefaultAsync(x => x.Id == verificationRecord.Id);

        Assert.NotNull(assertVerificationRecord);
        Assert.NotNull(assertVerificationRecord.AttemptedAt);
        Assert.Null(assertVerificationRecord.ValidatedAt);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, assertVerificationRecord.Status);
        Assert.NotNull(assertVerificationRecord.ResponseDetails);
        Assert.Equal(((int)HttpStatusCode.OK).ToString(), assertVerificationRecord.ResponseStatusCode);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, assertVerificationRecord.Subscription.OnboardStatus);
    }
}
