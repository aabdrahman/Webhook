using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Services;
using WebHook.Infrastructure.Utilities;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class StaleClaimedDeliverReleaseWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetryDeliveresAfterFailedConfiguration _retryDeliveresAfterFailedConfiguration;
    private readonly WorkerLivenessTracker _workerLivenessTracker;

    public StaleClaimedDeliverReleaseWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<RetryDeliveresAfterFailedConfiguration> optionsMonitor, WorkerLivenessTracker workerLivenessTracker)
    {
        _scopeFactory = scopeFactory;
        _retryDeliveresAfterFailedConfiguration = optionsMonitor.CurrentValue;
        _logger = Log.ForContext("ClassName", nameof(StaleClaimedDeliverReleaseWorker));
        _workerLivenessTracker = workerLivenessTracker;
    }

    private ILogger _logger;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StartAsync)).Information("Worker to process any possible stale claimed deliveries started successfully.....");
        await base.StartAsync(cancellationToken);
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.ForContext("MethodName", nameof(StopAsync)).Information("Worker to process any possible stale claimed deliveries is stopping.....");
        return base.StopAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        //Create an instance of the periodic timer.
        using var periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(_retryDeliveresAfterFailedConfiguration.StaleDeliveryReleaseIntervalSeconds));

        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                //Begin stale claimed deliveries processing.
                _logger.Information("Begin processing stale claimed deliveries....");
                _workerLivenessTracker.UpdateHeartBeat();
                //Create the scope and fetch all required services. The config is fethced at every point in time to ensue it picks up any changes made to the config(if any) afetr the last run.
                await using var scope = _scopeFactory.CreateAsyncScope();

                var processorService = scope.ServiceProvider.GetRequiredService<StaleClaimedDeliveryReleaseService>();
                var workerConfig = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>();

                var processorResult = await processorService.ProcessStaleDeliveriesAsync(lockDurationSeconds: workerConfig.CurrentValue.DeliveryLockDuration, ct: stoppingToken);

                _logger.Information("Stale claimed deliveries processed. Processor result - {0}", processorResult);

            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurred whle processing stale delievries background worker....");

            }
        }
    }
}
