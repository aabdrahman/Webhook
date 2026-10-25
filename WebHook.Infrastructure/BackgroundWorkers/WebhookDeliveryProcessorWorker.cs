using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.BackgroundWorkers;

/// <summary>
/// Periodically makes the first delivery attempt for <see cref="Core.Constants.WebhookDeliveryStatus.Pending"/>
/// deliveries through <see cref="WebhookDeliveryProcessorService"/>.
/// </summary>
/// <remarks>
/// Each instance claims deliveries under its own worker ID, generated at startup. The tick
/// interval is read once; the batch size, lock duration and slow-endpoint threshold are
/// re-read every tick.
/// </remarks>
public class WebhookDeliveryProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WebhookDeliveryWorkerConfiguration> _optionsMonitor;
    private readonly WorkerLivenessTracker _workerLivenessTracker;

    /// <summary>
    /// Initialises a new instance of the <see cref="WebhookDeliveryProcessorWorker"/> class.
    /// </summary>
    /// <param name="scopeFactory">Creates a scope, and so a fresh processor service, per tick.</param>
    /// <param name="optionsMonitor">Delivery processor settings.</param>
    /// <param name="workerLivenessTracker">Receives a heartbeat on every tick.</param>
    public WebhookDeliveryProcessorWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<WebhookDeliveryWorkerConfiguration> optionsMonitor, WorkerLivenessTracker workerLivenessTracker)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _workerLivenessTracker = workerLivenessTracker;

        _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryProcessorWorker));
        
        //_timeout = TimeSpan.FromSeconds(optionsMonitor.CurrentValue.DeliveryProcessorIntervalSeconds);
    }

    private ILogger _logger;
    //private TimeSpan _timeout;
    private Guid workerId;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StartAsync)).Information("WebhookDeliveryProcessorWorker starting. Tick interval: {0}s", 0);
        workerId = Guid.CreateVersion7(DateTimeOffset.UtcNow);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StopAsync)).Information("WebhookDeliveryProcessorWorker stopping at {Time}.", DateTimeOffset.UtcNow);
        return base.StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.DeliveryProcessorIntervalSeconds));

        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.Information("Beginning webhook delivery processing...");
                _workerLivenessTracker.UpdateHeartBeat();
                using var scope = _scopeFactory.CreateScope();

                var deliveryProcessor = scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessorService>();

                var processorConfig = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<WebhookDeliveryWorkerConfiguration>>();

                await deliveryProcessor.ProcessPendingDeliveriesAsync(totalToProcess: processorConfig.CurrentValue.TotalBatchSize, ct: stoppingToken, lockDuration: processorConfig.CurrentValue.DeliveryLockDuration, workerId: workerId.ToString(), thresholdDuration: processorConfig.CurrentValue.ThresholdDuration);

                _logger.Information("Webhook delivery processing completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An unexpected error occurred while processing webhook deliveries.");
            }
        }

        _logger.Information("Webhook delivery background service is stopping.");
    }
}
