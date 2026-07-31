using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using WebHook.Core.Entities.ConfigurationModels;
using WebHook.Infrastructure.Services;

namespace WebHook.Infrastructure.BackgroundWorkers;

public class RetryPendingDeieveriesWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetryDeliveresAfterFailedConfiguration _retryDeliveresAfterFailedConfiguration;

    public RetryPendingDeieveriesWorker(IServiceScopeFactory scopeFactory, IOptionsMonitor<RetryDeliveresAfterFailedConfiguration> optionsMonitor)
    {
        _scopeFactory = scopeFactory;
        _retryDeliveresAfterFailedConfiguration = optionsMonitor.CurrentValue;
        _logger = Log.ForContext("ClassName", nameof(RetryPendingDeieveriesWorker));
    }

    private ILogger _logger;


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {

                _logger.Information("Begin processing failed webhook deliveries.....");
                using var scope = _scopeFactory.CreateScope();
                var retryService = scope.ServiceProvider.GetRequiredService<RetryAfterPendingService>();
                var workerConfig = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<RetryDeliveresAfterFailedConfiguration>>();

                var retryDeliveresAfterFailedConfiguration = workerConfig.CurrentValue;

                await retryService.RunRetryAfterFirstAttemptAsync(ct: stoppingToken, totalAttempts: retryDeliveresAfterFailedConfiguration.TotalBatchSize,
                                                                    maximumAttemptCount: retryDeliveresAfterFailedConfiguration.MaximumAttendedCount, thresholdDuration: retryDeliveresAfterFailedConfiguration.ThresholdDuration);

                _logger.Information("Failed webhook deliveries processed successfully....");
                
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An error occurrred while processing failed webhook deliveries......");
            }
        }

       
    }
}
