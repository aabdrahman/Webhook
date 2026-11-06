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


    // ------------------------------------------------------------------------------------------------
    // Handshake tests. The fake receivers below behave like a real subscriber: they see only the HTTP
    // request (never the database), verify the platform's signature, and answer from the request alone.
    // ------------------------------------------------------------------------------------------------

    private static readonly SignatureService Signer = new();

    private async Task<(WebhookSubscription Subscription, WebhookSubscriptionVerification Verification, string Secret)> SeedPendingVerificationAsync(
        RepositoryContext ctx, string url = "https://example.com/hooks", DateTimeOffset? createdAt = null)
    {
        var subscription = BuildEntity("Handshake Subscription", eventIds: [_webHookEventCatalogs.First().Id], url: url, onboardStatus: SubscriptionOnboardStatus.Inactive);
        var verification = BuildSubscriptionVerificationEntity(subscriptionId: subscription.Id);

        if (createdAt.HasValue)
            verification.CreatedAt = createdAt.Value;

        subscription.WebhookSubscriptionVerifications.Add(verification);

        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        string secret = _serviceProvider.GetRequiredService<IEncryptionService>().Decrypt(subscription.SecretKey);
        return (subscription, verification, secret);
    }

    private async Task<WebhookSubscriptionVerification> ReloadAsync(Guid verificationId)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await ctx.WebhookSubscriptionVerifications.Include(x => x.Subscription).FirstAsync(x => x.Id == verificationId);
    }

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string TokenFrom(CapturedRequest request)
        => JsonDocument.Parse(request.Body).RootElement.GetProperty("challenge").GetString()!;

    /// <summary>True when the request carries a valid platform signature over "{timestamp}.{body}".</summary>
    private static bool HasValidPlatformSignature(CapturedRequest request, string secret)
        => request.Headers.TryGetValue("X-Webhook-Timestamp", out var timestamp)
           && request.Headers.TryGetValue("X-Webhook-Challenge-Signature", out var signature)
           && long.TryParse(timestamp, out var ts)
           && Signer.VerifySignature(request.Body, signature, secret, ts);

    /// <summary>A receiver that follows the documented contract.</summary>
    private static Func<CapturedRequest, HttpResponseMessage> CorrectReceiver(string secret, string propertyName = "response") => request =>
    {
        if (!HasValidPlatformSignature(request, secret))
            return JsonResponse("{}", HttpStatusCode.Unauthorized);

        string answer = Signer.GenerateSignature(SubscriptionVerificationProcessorService.ChallengeResponsePayloadPrefix + TokenFrom(request), secret);
        return JsonResponse($"{{\"{propertyName}\":\"{answer}\"}}");
    };

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ReceiverFollowsContract_ActivatesSubscription()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (subscription, verification, secret) = await SeedPendingVerificationAsync(ctx);

        var handler = new MockHttpMessageHandler(CorrectReceiver(secret));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, result.Status);
        Assert.NotNull(result.AttemptedAt);
        Assert.NotNull(result.ValidatedAt);
        Assert.Equal("200", result.ResponseStatusCode);
        Assert.Equal(SubscriptionOnboardStatus.Active, result.Subscription.OnboardStatus);
        Assert.True(result.Subscription.IsActive);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_Request_CarriesTokenInBodyAndSignatureOverTimestampAndBody()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, secret) = await SeedPendingVerificationAsync(ctx);

        var handler = new MockHttpMessageHandler(CorrectReceiver(secret));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(verification.ChallengeToken, TokenFrom(request));
        Assert.StartsWith("application/json", request.Headers["Content-Type"]);
        Assert.True(HasValidPlatformSignature(request, secret));

        // The timestamp is current, and the secret itself is never sent.
        Assert.InRange(long.Parse(request.Headers["X-Webhook-Timestamp"]), DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 10, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 10);
        Assert.DoesNotContain(secret, request.Body);
        Assert.DoesNotContain(secret, string.Join(";", request.Headers.Values));
    }

    [Theory]
    [InlineData("https://example.com/hooks", "https://example.com/hooks/challenge")]
    [InlineData("https://example.com/hooks/", "https://example.com/hooks/challenge")]
    [InlineData("https://example.com/", "https://example.com/challenge")]
    [InlineData("https://example.com", "https://example.com/challenge")]
    [InlineData("https://example.com/hooks?tenant=1", "https://example.com/hooks/challenge?tenant=1")]
    public async Task ProcessSignatureVerificationAsync_BuildsChallengeUrlRegardlessOfTrailingSlash(string callbackUrl, string expectedUrl)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, secret) = await SeedPendingVerificationAsync(ctx, url: callbackUrl);

        var handler = new MockHttpMessageHandler(CorrectReceiver(secret));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        Assert.Equal(expectedUrl, handler.LastRequest!.Uri.ToString());
    }

    [Theory]
    [InlineData("response")]
    [InlineData("Response")]
    public async Task ProcessSignatureVerificationAsync_ResponsePropertyCasingIsIgnored(string propertyName)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, secret) = await SeedPendingVerificationAsync(ctx);

        await CreateSut(ctx, new MockHttpMessageHandler(CorrectReceiver(secret, propertyName))).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await ReloadAsync(verification.Id)).Status);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ReceiverSignsWithWrongSecret_Fails()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        // Answers correctly in shape, but without knowing the subscription secret.
        var handler = new MockHttpMessageHandler(request => JsonResponse(
            $"{{\"response\":\"{Signer.GenerateSignature(SubscriptionVerificationProcessorService.ChallengeResponsePayloadPrefix + TokenFrom(request), "not-the-secret")}\"}}"));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Null(result.ValidatedAt);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ReceiverEchoesRequestBack_Fails()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        // A reflecting server: returns the token and the signature header it was given, knowing no secret.
        var handler = new MockHttpMessageHandler(request => JsonResponse(
            $"{{\"challenge\":\"{TokenFrom(request)}\",\"response\":\"{request.Headers["X-Webhook-Challenge-Signature"]}\"}}"));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ReceiverReturnsUnsignedToken_Fails()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        var handler = new MockHttpMessageHandler(request => JsonResponse($"{{\"response\":\"{TokenFrom(request)}\"}}"));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, (await ReloadAsync(verification.Id)).Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"response\":\"\"}")]
    [InlineData("{\"response\":null}")]
    [InlineData("not json at all")]
    [InlineData("[]")]
    public async Task ProcessSignatureVerificationAsync_MalformedOrEmptyResponse_Fails(string responseBody)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        await CreateSut(ctx, new MockHttpMessageHandler(HttpStatusCode.OK, responseBody)).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
        Assert.NotNull(result.AttemptedAt);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ExpiredChallenge_FailsWithoutSendingRequest()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var createdAt = DateTimeOffset.UtcNow - SubscriptionVerificationProcessorService.ChallengeValidity - TimeSpan.FromMinutes(1);
        var (_, verification, secret) = await SeedPendingVerificationAsync(ctx, createdAt: createdAt);

        var handler = new MockHttpMessageHandler(CorrectReceiver(secret));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Null(result.AttemptedAt);
        Assert.Contains("expired", result.ResponseDetails, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_ReceiverTimesOut_FailsWith408()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        // HttpClient reports its own timeout as a TaskCanceledException caused by a TimeoutException.
        var handler = new MockHttpMessageHandler(new TaskCanceledException("The request timed out.", new TimeoutException()));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal("408", result.ResponseStatusCode);
        Assert.NotNull(result.AttemptedAt);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_OversizedResponse_IsTruncatedAndFails()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, _) = await SeedPendingVerificationAsync(ctx);

        var handler = new MockHttpMessageHandler(HttpStatusCode.OK, new string('x', SubscriptionVerificationProcessorService.MaxResponseBytes * 4));

        await CreateSut(ctx, handler).ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        var result = await ReloadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal(SubscriptionVerificationProcessorService.MaxResponseBytes, result.ResponseDetails.Length);
    }

    [Fact]
    public async Task ProcessSignatureVerificationAsync_AlreadyProcessed_DoesNotSendAgain()
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        var (_, verification, secret) = await SeedPendingVerificationAsync(ctx);

        var handler = new MockHttpMessageHandler(CorrectReceiver(secret));
        var sut = CreateSut(ctx, handler);

        await sut.ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);
        await sut.ProcessSignatureVerificationAsync(verification.Id, CancellationToken.None);

        Assert.Equal(1, handler.CallCount);
    }
}
