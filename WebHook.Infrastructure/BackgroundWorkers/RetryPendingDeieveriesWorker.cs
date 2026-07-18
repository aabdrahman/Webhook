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
        periodicTimer = new PeriodicTimer(TimeSpan.FromSeconds(60));
    }

    private ILogger _logger;
    private PeriodicTimer periodicTimer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger = _logger.ForContext("MethodName", nameof(ExecuteAsync));

        try
        {
            while (!stoppingToken.IsCancellationRequested && await periodicTimer.WaitForNextTickAsync())
            {
                _logger.Information("Begin processing failed webhook deliveries.....");
                using var scope = _scopeFactory.CreateScope();
                var retryService = scope.ServiceProvider.GetRequiredService<RetryAfterPendingService>();

                await retryService.RunRetryAfterFirstAttemptAsync(ct: stoppingToken, totalAttempts: _retryDeliveresAfterFailedConfiguration.TotalBatchSize,
                                                                    maximumAttemptCount: _retryDeliveresAfterFailedConfiguration.MaximumAttendedCount, thresholdDuration: _retryDeliveresAfterFailedConfiguration.ThresholdDuration);

                _logger.Information("Failed webhook deliveries processed successfully....");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurrred while processing failed webhook deliveries......");
        }
    }
}
