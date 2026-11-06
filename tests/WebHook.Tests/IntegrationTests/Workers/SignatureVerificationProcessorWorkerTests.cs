using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Core.Interfaces.Helpers;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Security;
using WebHook.Infrastructure.Services;
using WebHook.Tests.Utilities;

namespace WebHook.IntegrationTests.BackgroundWorkers;

/// <summary>
/// Integration tests for <see cref="SignatureVerificationProcessorWorker"/> against a real PostgreSQL container.
/// The worker reads <see cref="SubscriptionVerificationRaised"/> events from a channel and runs the callback URL
/// handshake for each one. The subscriber is faked at the HTTP layer: it sees only the request, as a real receiver would.
/// </summary>
public sealed class SignatureVerificationProcessorWorkerTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private static readonly SignatureService Signer = new();

    private readonly PostgreSqlFixture _fixture;
    private readonly Channel<SubscriptionVerificationRaised> _channel = Channel.CreateUnbounded<SubscriptionVerificationRaised>();
    private ServiceProvider _serviceProvider = null!;
    private string _encryptedSecret = string.Empty;
    private string _secret = string.Empty;

    public SignatureVerificationProcessorWorkerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Log.Logger = new LoggerConfiguration().CreateLogger();
    }

    public async Task InitializeAsync()
    {
        Environment.SetEnvironmentVariable("env_webhook_encrypt_key", "00AF7F388E47B0AC211E931842DAFA7422648881071244DF484725D249D9E5F1");
        Environment.SetEnvironmentVariable("env_webhook_encrypt_iv", "315BDF36FBC81E00D668494E187AC4A5");

        var services = new ServiceCollection();
        services.AddDbContext<RepositoryContext>(opts => opts.UseNpgsql(_fixture.ConnectionString));
        services.AddScoped<ISignatureService, SignatureService>();
        services.AddScoped<IEncryptionService, EncryptionService>();
        services.Configure<EventRaisedWorkerConfiguration>(opts => opts.ProcessingIntervalInSeconds = 1);

        _serviceProvider = services.BuildServiceProvider();

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        _secret = Random.Shared.GetHexString(32);
        _encryptedSecret = scope.ServiceProvider.GetRequiredService<IEncryptionService>().Encrypt(_secret);
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _disposables)
            await provider.DisposeAsync();

        await _serviceProvider.DisposeAsync();
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Builds a worker whose processor uses <paramref name="handler"/> for the callback calls.</summary>
    private TestableVerificationWorker CreateWorker(HttpMessageHandler handler, Func<IServiceScopeFactory, IServiceScopeFactory>? decorateScopeFactory = null)
    {
        var workerServices = new ServiceCollection();
        workerServices.AddDbContext<RepositoryContext>(opts => opts.UseNpgsql(_fixture.ConnectionString));
        workerServices.AddScoped<ISignatureService, SignatureService>();
        workerServices.AddScoped<IEncryptionService, EncryptionService>();
        workerServices.AddSingleton<IHttpClientFactory>(new HandlerHttpClientFactory(handler));
        workerServices.AddScoped<SubscriptionVerificationProcessorService>();
        var provider = workerServices.BuildServiceProvider();
        _disposables.Add(provider);

        IServiceScopeFactory scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        if (decorateScopeFactory is not null)
            scopeFactory = decorateScopeFactory(scopeFactory);

        return new TestableVerificationWorker(_channel, scopeFactory, _serviceProvider.GetRequiredService<IOptionsMonitor<EventRaisedWorkerConfiguration>>());
    }

    private readonly List<ServiceProvider> _disposables = [];

    /// <summary>Runs the worker until <paramref name="condition"/> holds or the timeout elapses, then stops it. Returns whether the condition held.</summary>
    private async Task<bool> RunUntilAsync(TestableVerificationWorker worker, Func<Task<bool>> condition, int timeoutMs = 15_000)
    {
        using var cts = new CancellationTokenSource();
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token));

        bool met = await WaitUntilAsync(condition, timeoutMs);

        cts.Cancel();
        try { await executeTask; }
        catch (OperationCanceledException) { }

        return met;
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }
        return await condition();
    }

    private async Task<WebhookSubscriptionVerification> SeedAsync(
        WebhookSubscriptionVerificationStatus status = WebhookSubscriptionVerificationStatus.Pending,
        SubscriptionOnboardStatus onboardStatus = SubscriptionOnboardStatus.Inactive,
        TimeSpan? age = null,
        string url = "https://example.com/hooks")
    {
        var subscription = new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            Name = $"Subscription {Guid.NewGuid():N}",
            IsActive = true,
            CallbackUrl = url,
            SecretKey = _encryptedSecret,
            SubscribedFields = [],
            OnboardStatus = onboardStatus
        };

        var verification = new WebhookSubscriptionVerification
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscription.Id,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow - (age ?? TimeSpan.Zero),
            ChallengeToken = Random.Shared.GetHexString(64)
        };

        subscription.WebhookSubscriptionVerifications.Add(verification);

        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.WebhookSubscriptions.AddAsync(subscription);
        await ctx.SaveChangesAsync();

        return verification;
    }

    private async Task<WebhookSubscriptionVerification> LoadAsync(Guid verificationId)
    {
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        return await ctx.WebhookSubscriptionVerifications.Include(x => x.Subscription).AsNoTracking().FirstAsync(x => x.Id == verificationId);
    }

    private Func<Task<bool>> IsCompleted(Guid verificationId) => async () =>
        (await LoadAsync(verificationId)).Status != WebhookSubscriptionVerificationStatus.Pending;

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string TokenFrom(CapturedRequest request)
        => JsonDocument.Parse(request.Body).RootElement.GetProperty("challenge").GetString()!;

    /// <summary>A receiver that follows the documented contract: verifies the platform signature, then signs the challenge.</summary>
    private Func<CapturedRequest, HttpResponseMessage> CorrectReceiver() => request =>
    {
        bool authentic = request.Headers.TryGetValue("X-Webhook-Timestamp", out var timestamp)
                         && request.Headers.TryGetValue("X-Webhook-Challenge-Signature", out var signature)
                         && long.TryParse(timestamp, out var ts)
                         && Signer.VerifySignature(request.Body, signature, _secret, ts);

        if (!authentic)
            return JsonResponse("{}", HttpStatusCode.Unauthorized);

        string answer = Signer.GenerateSignature(SubscriptionVerificationProcessorService.ChallengeResponsePayloadPrefix + TokenFrom(request), _secret);
        return JsonResponse($"{{\"response\":\"{answer}\"}}");
    };

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task QueuedVerification_ReceiverAnswersCorrectly_SubscriptionBecomesActive()
    {
        var verification = await SeedAsync();
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        bool completed = await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        Assert.True(completed, "The worker did not process the queued verification in time.");
        var result = await LoadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, result.Status);
        Assert.NotNull(result.AttemptedAt);
        Assert.NotNull(result.ValidatedAt);
        Assert.Equal(SubscriptionOnboardStatus.Active, result.Subscription.OnboardStatus);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task QueuedVerification_ReceiverSendsToChallengePath_WithTokenAndValidSignature()
    {
        var verification = await SeedAsync(url: "https://example.com/hooks/");
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        var request = handler.LastRequest!;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://example.com/hooks/challenge", request.Uri.ToString());
        Assert.Equal(verification.ChallengeToken, TokenFrom(request));
        Assert.DoesNotContain(_secret, request.Body);
    }

    [Fact]
    public async Task QueuedVerification_ReceiverAnswersWrong_RecordFailsAndSubscriptionStaysInactive()
    {
        var verification = await SeedAsync();
        var handler = new MockHttpMessageHandler(request => JsonResponse("{\"response\":\"sha256=deadbeef\"}"));
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        var result = await LoadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Null(result.ValidatedAt);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task QueuedVerification_ReceiverReturns500_RecordFailsWithStatusCode()
    {
        var verification = await SeedAsync();
        var handler = new MockHttpMessageHandler(HttpStatusCode.InternalServerError, "boom");
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        var result = await LoadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal("500", result.ResponseStatusCode);
        Assert.Equal("boom", result.ResponseDetails);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task QueuedVerification_ReceiverUnreachable_RecordFails()
    {
        var verification = await SeedAsync();
        var handler = new MockHttpMessageHandler(new HttpRequestException("The remote name could not be resolved"));
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        var result = await LoadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.NotNull(result.AttemptedAt);
    }

    [Fact]
    public async Task QueuedVerification_ChallengeExpiredWhileQueued_FailsWithoutCallingReceiver()
    {
        var age = SubscriptionVerificationProcessorService.ChallengeValidity + TimeSpan.FromMinutes(1);
        var verification = await SeedAsync(age: age);
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        var result = await LoadAsync(verification.Id);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Failed, result.Status);
        Assert.Equal(0, handler.CallCount);
        Assert.Equal(SubscriptionOnboardStatus.Inactive, result.Subscription.OnboardStatus);
    }

    [Fact]
    public async Task MultipleQueuedVerifications_AreAllProcessed()
    {
        var verifications = new List<WebhookSubscriptionVerification>();
        for (int i = 0; i < 5; i++)
            verifications.Add(await SeedAsync());

        foreach (var verification in verifications)
            await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        var handler = new MockHttpMessageHandler(CorrectReceiver());

        bool completed = await RunUntilAsync(CreateWorker(handler), async () =>
        {
            foreach (var verification in verifications)
                if ((await LoadAsync(verification.Id)).Status == WebhookSubscriptionVerificationStatus.Pending)
                    return false;
            return true;
        });

        Assert.True(completed);
        foreach (var verification in verifications)
            Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await LoadAsync(verification.Id)).Status);
        Assert.Equal(5, handler.CallCount);
    }

    [Fact]
    public async Task VerificationQueuedWhileWorkerIsRunning_IsProcessed()
    {
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        var worker = CreateWorker(handler);
        var verification = await SeedAsync();

        using var cts = new CancellationTokenSource();
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token));

        // Let the worker pass its first timer tick and start waiting on the empty channel.
        await Task.Delay(2_500);
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        bool completed = await WaitUntilAsync(IsCompleted(verification.Id), 10_000);

        cts.Cancel();
        try { await executeTask; } catch (OperationCanceledException) { }

        Assert.True(completed);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await LoadAsync(verification.Id)).Status);
    }

    [Fact]
    public async Task UnknownVerificationId_DoesNotStopTheWorker()
    {
        var verification = await SeedAsync();
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(Guid.NewGuid()));
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        bool completed = await RunUntilAsync(CreateWorker(handler), IsCompleted(verification.Id));

        Assert.True(completed);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await LoadAsync(verification.Id)).Status);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task AlreadyCompletedVerification_IsNotSentAgain()
    {
        var alreadyDone = await SeedAsync(WebhookSubscriptionVerificationStatus.Success, SubscriptionOnboardStatus.Active);
        var sentinel = await SeedAsync();
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(alreadyDone.Id));
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(sentinel.Id));

        bool completed = await RunUntilAsync(CreateWorker(handler), IsCompleted(sentinel.Id));

        Assert.True(completed);
        // Only the sentinel produced a call; the finished record was skipped.
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await LoadAsync(alreadyDone.Id)).Status);
    }

    [Fact]
    public async Task FailureWhileHandlingOneEvent_DoesNotPreventTheNextOne()
    {
        var first = await SeedAsync();
        var second = await SeedAsync();
        var handler = new MockHttpMessageHandler(CorrectReceiver());
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(first.Id));
        await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(second.Id));

        // The first CreateScope call throws, which the worker must catch and log before moving on.
        var worker = CreateWorker(handler, inner => new FailFirstScopeFactory(inner));

        bool completed = await RunUntilAsync(worker, IsCompleted(second.Id));

        Assert.True(completed);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Pending, (await LoadAsync(first.Id)).Status);
        Assert.Equal(WebhookSubscriptionVerificationStatus.Success, (await LoadAsync(second.Id)).Status);
    }

    [Fact]
    public async Task QueuedVerifications_AreProcessedOneAtATime()
    {
        var verifications = new List<WebhookSubscriptionVerification>();
        for (int i = 0; i < 3; i++)
            verifications.Add(await SeedAsync());

        foreach (var verification in verifications)
            await _channel.Writer.WriteAsync(new SubscriptionVerificationRaised(verification.Id));

        var handler = new ConcurrencyTrackingHandler(CorrectReceiver());

        bool completed = await RunUntilAsync(CreateWorker(handler), async () =>
        {
            foreach (var verification in verifications)
                if ((await LoadAsync(verification.Id)).Status == WebhookSubscriptionVerificationStatus.Pending)
                    return false;
            return true;
        });

        Assert.True(completed);
        Assert.Equal(3, handler.Calls);
        Assert.Equal(1, handler.MaxConcurrent);
    }

    [Fact]
    public async Task Cancellation_StopsTheWorkerPromptly()
    {
        var worker = CreateWorker(new MockHttpMessageHandler(CorrectReceiver()));
        using var cts = new CancellationTokenSource();
        var executeTask = Task.Run(() => worker.RunAsync(cts.Token));

        await Task.Delay(1_500);
        cts.Cancel();

        var finished = await Task.WhenAny(executeTask, Task.Delay(5_000));
        Assert.Same(executeTask, finished);
        Assert.False(executeTask.IsFaulted);
    }

    // ------------------------------------------------------------------ test doubles

    private sealed class TestableVerificationWorker(
        Channel<SubscriptionVerificationRaised> channel,
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<EventRaisedWorkerConfiguration> config)
        : SignatureVerificationProcessorWorker(channel, scopeFactory, config)
    {
        // Exposes the protected ExecuteAsync so tests can run the loop directly.
        public Task RunAsync(CancellationToken ct) => ExecuteAsync(ct);
    }

    private sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FailFirstScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
    {
        private int _calls;

        public IServiceScope CreateScope()
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("Simulated scope creation failure.");

            return inner.CreateScope();
        }
    }

    /// <summary>Answers like <see cref="MockHttpMessageHandler"/> but takes time, and records the highest number of requests in flight.</summary>
    private sealed class ConcurrencyTrackingHandler(Func<CapturedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _current;
        public int MaxConcurrent;
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _current);
            int seen;
            while ((seen = MaxConcurrent) < now)
                Interlocked.CompareExchange(ref MaxConcurrent, now, seen);

            Interlocked.Increment(ref Calls);
            try
            {
                var captured = await CapturedRequest.FromAsync(request);
                await Task.Delay(200, cancellationToken);
                return responder(captured);
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
