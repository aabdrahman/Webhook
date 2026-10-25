using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.BackgroundWorkers;

/// <summary>
/// Periodically retries <see cref="Core.Constants.WebhookDeliveryStatus.Failed"/> deliveries
/// through <see cref="RetryAfterPendingService"/>.
/// </summary>
/// <remarks>
/// Each instance claims deliveries under its own worker ID, generated at startup. The tick
/// interval is read once at construction; the batch size and other limits are re-read every tick.
/// </remarks>
public class RetryPendingDeliveriesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetryDeliveresAfterFailedConfiguration _retryDeliveresAfterFailedConfiguration;
    private readonly WorkerLivenessTracker _workerLivenessTracker;

    /// <summary>
    /// Initialises a new instance of the <see cref="RetryPendingDeliveriesWorker"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates a scope, and so a fresh retry service, per tick.</param>
    /// <param name="optionsMonitor">Retry settings; supplies the tick interval.</param>
    /// <param name="workerLivenessTracker">Receives a heartbeat on every tick.</param>
    public RetryPendingDeliveriesWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<RetryDeliveresAfterFailedConfiguration> optionsMonitor, WorkerLivenessTracker workerLivenessTracker)
    {
        _scopeFactory = scopeFactory;
        _retryDeliveresAfterFailedConfiguration = optionsMonitor.CurrentValue;
        _logger = Log.ForContext("ClassName", nameof(RetryPendingDeliveriesWorker));
        _workerLivenessTracker = workerLivenessTracker;
    }

    private ILogger _logger;
    private Guid _workerId;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StartAsync)).Information("Worker has started successfully.....");
        _workerId = Guid.CreateVersion7(DateTimeOffset.UtcNow);
        await base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StopAsync)).Information("Worker is stopping.....");
       
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(_retryDeliveresAfterFailedConfiguration.RetryFailedDeliveryIntervalSeconds));

        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _workerLivenessTracker.UpdateHeartBeat();
                _logger.Information("Begin processing failed webhook deliveries.....");
                using var scope = _scopeFactory.CreateScope();
                var retryService = scope.ServiceProvider.GetRequiredService<RetryAfterPendingService>();
                var workerConfig = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>();

                var retryDeliveresAfterFailedConfiguration = workerConfig.CurrentValue;

                await retryService.RunRetryAfterFirstAttemptAsync(ct: stoppingToken, totalAttempts: retryDeliveresAfterFailedConfiguration.TotalBatchSize,
                                                                    maximumAttemptCount: retryDeliveresAfterFailedConfiguration.MaximumAttendedCount, thresholdDuration: retryDeliveresAfterFailedConfiguration.ThresholdDuration,
                                                                    lockDuration: retryDeliveresAfterFailedConfiguration.DeliveryLockDuration, workerId: _workerId.ToString());

                _logger.Information("Failed webhook deliveries processed successfully....");

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurrred while processing failed webhook deliveries......");
            }
        }


    }
}
