using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Threading.Channels;
using WebHook.Core.Constants;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Core.EventContracts.Events;
using WebHook.Infrastructure.Data_Persistence;

namespace WebHook.Infrastructure.BackgroundWorkers;

/// <summary>
/// A background service that runs on a periodic timer and re-queues
/// <see cref="WebhookEvent"/> records that have been stuck in
/// <see cref="WebHookEventStatus.Pending"/> status beyond the configured
/// threshold, pushing their IDs into the <see cref="Channel{EventRaised}"/>
/// so <see cref="EventRaisedWorker"/> can pick them up and process them.
/// </summary>
/// <remarks>
/// <para>
/// This worker complements <see cref="StuckProcessingCleanerBackgroundService"/>
/// which handles events stuck in <c>Processing</c> status. This worker
/// handles events that never left <c>Pending</c> — meaning they were
/// persisted but the channel write failed or the worker that should have
/// read them crashed before doing so.
/// </para>
/// <para>
/// Both the tick interval and the pending threshold are configurable via
/// <see cref="WorkerConfiguration"/> so they can be tuned at runtime
/// without redeploying.
/// </para>
/// </remarks>
public sealed class PendingRaisedEventsWorker : BackgroundService
{
    private readonly Channel<EventRaised> _eventRaisedChannel;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptionsMonitor<PendingRaisedEventsWorkerConfiguration> _options;

    private ILogger _logger;
    private const string _className = "ClassName";
    private const string _methodName = "MethodName";

    /// <summary>
    /// Initializes a new instance of <see cref="PendingRaisedEventsWorker"/>.
    /// </summary>
    /// <param name="eventRaisedChannel">
    /// The channel into which pending event IDs are written so
    /// <see cref="EventRaisedWorker"/> can process them.
    /// </param>
    /// <param name="serviceScopeFactory">
    /// Used to create an isolated scope per tick so the scoped
    /// <see cref="RepositoryContext"/> is properly disposed after each run.
    /// </param>
    /// <param name="options">
    /// Configuration controlling the tick interval and the pending threshold.
    /// Read inside the loop so runtime config changes are picked up without
    /// restarting the worker.
    /// </param>
    public PendingRaisedEventsWorker(Channel<EventRaised> eventRaisedChannel, IServiceScopeFactory serviceScopeFactory, IOptionsMonitor<PendingRaisedEventsWorkerConfiguration> options)
    {
        _eventRaisedChannel = eventRaisedChannel;
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _logger = Log.ForContext(_className, nameof(PendingRaisedEventsWorker));
    }

    /// <inheritdoc/>
    public override Task StartAsync(CancellationToken cancellationToken)
    {

        _logger.ForContext(_methodName, nameof(StartAsync)).Information("PendingRaisedEventsWorker starting. Interval: {IntervalSeconds}s | PendingThreshold: {ThresholdMinutes} minutes", _options.CurrentValue.PendingEventsWorkerIntervalSeconds, _options.CurrentValue.PendingEventsThresholdMinutes);

        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext(_methodName, nameof(StopAsync)).Information("PendingRaisedEventsWorker stopping at {Time}.", DateTimeOffset.UtcNow);

        return base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// The main execution loop. On each tick scans for
    /// <see cref="WebhookEvent"/> records stuck in
    /// <see cref="WebHookEventStatus.Pending"/> beyond the configured
    /// threshold and writes their IDs to the channel.
    /// </summary>
    /// <param name="stoppingToken">
    /// Triggered when the host is performing a graceful shutdown.
    /// </param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext(_methodName, nameof(ExecuteAsync));

        // PeriodicTimer created as a local using — disposed when ExecuteAsync exits
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.CurrentValue.PendingEventsWorkerIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.Information("Begin processing pending events.");

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var repositoryContext = scope.ServiceProvider.GetRequiredService<RepositoryContext>();

                // Read threshold inside loop so runtime config changes are respected
                var thresholdMinutes = _options.CurrentValue.PendingEventsThresholdMinutes;
                var thresholdDatetime = DateTimeOffset.UtcNow.AddMinutes(-thresholdMinutes);

                List<Guid> pendingIds = await repositoryContext.WebhookEvents
                                                    .Where(x => x.Status == WebHookEventStatus.Pending && x.CreatedAt <= thresholdDatetime)
                                                    .Select(x => x.Id)
                                                    .ToListAsync(stoppingToken);

                if (!pendingIds.Any())
                {
                    _logger.Information("No pending events older than {ThresholdMinutes} minutes found.", thresholdMinutes);
                    continue;
                }

                _logger.Information("{Count} pending event(s) found. Writing to channel...", pendingIds.Count);

                foreach (var pendingId in pendingIds)
                {
                    await _eventRaisedChannel.Writer.WriteAsync(new EventRaised(pendingId), stoppingToken);
                }

                _logger.Information("{Count} pending event(s) written to channel successfully.", pendingIds.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — exit cleanly
                break;
            }
            catch (Exception ex)
            {
                // Log and continue — one failure must not kill the worker
                _logger.Error(ex, "An error occurred while processing pending raised events.");
            }
        }

        _logger.Information("PendingRaisedEventsWorker has stopped.");
    }
}