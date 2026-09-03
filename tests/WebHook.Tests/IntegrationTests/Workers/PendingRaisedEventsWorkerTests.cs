using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.BackgroundWorkers;
using WebHook.Infrastructure.Data_Persistence;
using WebHook.Infrastructure.Utilities;

namespace WebHook.IntegrationTests.BackgroundWorkers;

public class PendingRaisedEventsWorkerTests : IClassFixture<PostgreSqlFixture>, IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    private IServiceProvider _serviceProvider = null!;
    private Channel<EventRaised> _channel = null!;

    public PendingRaisedEventsWorkerTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _channel = Channel.CreateUnbounded<EventRaised>();

        var services = new ServiceCollection();

        services.AddSingleton(_channel);

        services.AddDbContext<RepositoryContext>(options =>
        {
            options.UseNpgsql(_fixture.ConnectionString);
        });

        services.AddOptions<PendingRaisedEventsWorkerConfiguration>()
            .Configure(options =>
            {
                // Keep this small so the test does not wait 300 seconds.
                options.PendingEventsWorkerIntervalSeconds = 1;

                options.PendingEventsThresholdMinutes = 30;
            });

        services.AddSingleton(new WorkerLivenessTracker(timeSpan: TimeSpan.FromSeconds(15)));

        _serviceProvider = services.BuildServiceProvider();

        _serviceProvider = services.BuildServiceProvider();

        // Wipe and recreate schema so every test starts with a clean slate
        using var scope = _serviceProvider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<RepositoryContext>();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.EnsureCreatedAsync();

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_serviceProvider is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            (_serviceProvider as IDisposable)?.Dispose();
        }
    }

    [Fact]
    public async Task StartAsync_PendingEventOlderThanThreshold_WritesEventToChannel()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var webhookEvent = BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddMinutes(-60));

            await context.WebhookEvents.AddAsync(webhookEvent);
            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await WaitForConditionAsync(
                () => _channel.Reader.Count == 1,
                cts.Token);

            // Assert
            Assert.Equal(1, _channel.Reader.Count);

            var raisedEvent = await _channel.Reader.ReadAsync(cts.Token);

            Assert.Equal(eventId, raisedEvent.createdEventId);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_PendingEventWithinThreshold_DoesNotWriteToChannel()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var webhookEvent = BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5));

            await context.WebhookEvents.AddAsync(webhookEvent);
            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            // Give the worker enough time for at least one tick.
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token);

            // Assert
            Assert.Equal(0, _channel.Reader.Count);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_NonPendingEventOlderThanThreshold_DoesNotWriteToChannel()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            var webhookEvent = BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Processing, createdAt: DateTimeOffset.UtcNow.AddMinutes(-60));

            await context.WebhookEvents.AddAsync(webhookEvent);
            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token);

            // Assert
            Assert.Equal(0, _channel.Reader.Count);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_MultiplePendingEventsOlderThanThreshold_WritesAllEventsToChannel()
    {
        // Arrange
        var eventIds = Enumerable
                        .Range(1, 3)
                        .Select(_ => Guid.NewGuid())
                        .ToList();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            foreach (var eventId in eventIds)
            {
                await context.WebhookEvents.AddAsync(BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddMinutes(-60)));
            }

            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await WaitForConditionAsync(
                () => _channel.Reader.Count == eventIds.Count,
                cts.Token);

            // Assert
            Assert.Equal(eventIds.Count, _channel.Reader.Count);

            var queuedIds = new List<Guid>();

            while (_channel.Reader.TryRead(out var raisedEvent))
            {
                queuedIds.Add(raisedEvent.createdEventId);
            }

            Assert.Equal(eventIds.OrderBy(x => x), queuedIds.OrderBy(x => x));
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_NoQualifyingEvents_ChannelRemainsEmpty()
    {
        // Arrange
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            await context.WebhookEvents.AddAsync(BuildWebhookEvent(id: Guid.NewGuid(), status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddMinutes(-5)));

            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token);

            // Assert
            Assert.Equal(0, _channel.Reader.Count);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_WhenCancellationRequested_StopsGracefully()
    {
        // Arrange
        var worker = CreateWorker();

        using var cts = new CancellationTokenSource();

        // Act
        await worker.StartAsync(cts.Token);

        cts.Cancel();

        // Assert
        var exception = await Record.ExceptionAsync(() => worker.StopAsync(CancellationToken.None));

        Assert.Null(exception);
    }

    [Fact]
    public async Task StartAsync_EventExactlyAtThreshold_IsEligibleForRequeue()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context =
                scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            // Give a little margin because the worker calculates
            // UtcNow again when the query executes.
            var webhookEvent = BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddMinutes(-31));

            await context.WebhookEvents.AddAsync(webhookEvent);
            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await WaitForConditionAsync(
                () => _channel.Reader.Count == 1,
                cts.Token);

            // Assert
            var raisedEvent =
                await _channel.Reader.ReadAsync(cts.Token);

            Assert.Equal(eventId, raisedEvent.createdEventId);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StartAsync_QualifyingEvent_IsQueuedAsEventRaisedWithCorrectId()
    {
        // Arrange
        var eventId = Guid.NewGuid();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

            await context.WebhookEvents.AddAsync(BuildWebhookEvent(id: eventId, status: WebHookEventStatus.Pending, createdAt: DateTimeOffset.UtcNow.AddHours(-1)));

            await context.SaveChangesAsync();
        }

        var worker = CreateWorker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        await worker.StartAsync(cts.Token);

        try
        {
            await WaitForConditionAsync(
                () => _channel.Reader.Count == 1,
                cts.Token);

            var queuedEvent = await _channel.Reader.ReadAsync(cts.Token);

            // Assert
            Assert.Equal(eventId, queuedEvent.createdEventId);
        }
        finally
        {
            cts.Cancel();

            await worker.StopAsync(CancellationToken.None);
        }
    }

    private PendingRaisedEventsWorker CreateWorker()
    {
        var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();

        var options = _serviceProvider.GetRequiredService<IOptionsMonitor<PendingRaisedEventsWorkerConfiguration>>();

        var livelinessTracker = _serviceProvider.GetRequiredService<WorkerLivenessTracker>();

        return new PendingRaisedEventsWorker(_channel, scopeFactory, options, livelinessTracker);
    }

    private static WebhookEvent BuildWebhookEvent(Guid id, WebHookEventStatus status, DateTimeOffset createdAt)
    {
        return new WebhookEvent
        {
            Id = id,
            Status = status,
            CreatedAt = createdAt,

            // Add any other properties required by your entity/database.
            EventType = "CustomerCreated",
            PayLoad = "{}",
            Source = "IntegrationTest",
            CorrelationId = Guid.NewGuid()
        };
    }

    private static async Task WaitForConditionAsync(Func<bool> condition, CancellationToken cancellationToken, int timeoutMs = 10_000)
    {
        var start = DateTime.UtcNow;

        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((DateTime.UtcNow - start).TotalMilliseconds >= timeoutMs)
            {
                throw new TimeoutException("The expected worker condition was not reached within the timeout.");
            }

            await Task.Delay(50, cancellationToken);
        }
    }
}

