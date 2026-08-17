using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Services;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class WebhookDeliveryProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<WebhookDeliveryWorkerConfiguration> _optionsMonitor;

    public WebhookDeliveryProcessorWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<WebhookDeliveryWorkerConfiguration> optionsMonitor)
    {
        _scopeFactory = scopeFactory;
        _optionsMonitor = optionsMonitor;
        _logger = Log.ForContext("ClassName", nameof(WebhookDeliveryProcessorWorker));
        //_timeout = TimeSpan.FromSeconds(optionsMonitor.CurrentValue.DeliveryProcessorIntervalSeconds);
    }

    private ILogger _logger;
    //private TimeSpan _timeout;
    private Guid workerId;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Information("WebhookDeliveryProcessorWorker starting. Tick interval: {0}s", 0);
        workerId = Guid.CreateVersion7(DateTimeOffset.UtcNow);
        return base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Information("WebhookDeliveryProcessorWorker stopping at {Time}.", DateTimeOffset.UtcNow);
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(_optionsMonitor.CurrentValue.DeliveryProcessorIntervalSeconds));

        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _logger.Information("Beginning webhook delivery processing...");

                using var scope = _scopeFactory.CreateScope();

                var deliveryProcessor =
                    scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessorService>();

                var processorConfig = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<WebhookDeliveryWorkerConfiguration>>();

                await deliveryProcessor.ProcessPendingDeliveriesAsync(totalToProcess: processorConfig.CurrentValue.TotalBatchSize, ct: stoppingToken, lockDuration: processorConfig.CurrentValue.DeliveryLockDuration, workerId: workerId.ToString());

                _logger.Information("Webhook delivery processing completed.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex,
                    "An unexpected error occurred while processing webhook deliveries.");
            }
        }

        _logger.Information("Webhook delivery background service is stopping.");
    }
}
